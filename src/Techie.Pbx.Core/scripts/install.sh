#!/usr/bin/env bash
#
# TNPBX installer, part 1: everything a fresh Debian 12/13 server needs in order to be a
# TNPBX box, EXCEPT the web application itself.
#
# What this script does NOT do, by decision (D92): it does not deploy Techie.Pbx.Web, does not
# install tnpbx-web.service, and does not start Asterisk. The application is deployed by a
# separate later step, and it generates every file in /etc/asterisk on its first apply (D4, D7).
# Until that has happened there is no configuration for Asterisk to read, so this script leaves
# asterisk.service enabled but stopped (D93).
#
# Usage (as root on Debian 12 or 13):
#   ./install.sh              # install
#   ./install.sh --dry-run    # print every step instead of running it (use this for review)
#   ./install.sh --rebuild    # force an Asterisk rebuild even if the right version is installed
#
# The layout it produces is the one proven on the lab VM (D94): the asterisk and tnpbx system
# users, tnpbx in the asterisk group, and /etc/asterisk setgid 2770 root:asterisk, which is how
# the unprivileged web process gets to write generated config without root or sudo (D3, D18).

set -euo pipefail

ASTERISK_MAJOR="22"   # current LTS; bump when the next LTS ships
DOWNLOAD_BASE="https://downloads.asterisk.org/pub/telephony/asterisk"
TARBALL="asterisk-${ASTERISK_MAJOR}-current.tar.gz"
BUILD_DIR="/usr/src/asterisk-${ASTERISK_MAJOR}"
CORE_SOUNDS_URL="https://downloads.asterisk.org/pub/telephony/sounds/releases"
CORE_SOUNDS_VERSION="1.6.1"   # matches the GSM set make install ships

APP_USER="tnpbx"
APP_HOME="/opt/tnpbx"
APP_BIN_DIR="${APP_HOME}/bin"
APP_CONFIG_DIR="${APP_HOME}/Config"

# Voicemail transcription (D128), and every one of these is optional: see section 6. small.en is
# the English-only model, about 488 MB, and roughly real time on the hardware this targets — big
# enough to be worth reading, small enough to run on a PBX while it is also being a PBX.
WHISPER_DIR="${APP_HOME}/whisper"
WHISPER_MODEL="${WHISPER_DIR}/ggml-small.en.bin"
WHISPER_MODEL_URL="https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.en.bin"
WHISPER_REPO="https://github.com/ggml-org/whisper.cpp"
WHISPER_SOURCE_DIR="/usr/src/whisper.cpp"
ANNOUNCEMENTS_DIR="/var/lib/asterisk/sounds/tnpbx/announcements"
MOH_DIR="/var/lib/asterisk/moh"

# Music on hold is one directory per class now (D122), and this is the class that ships: the
# application creates the row, this script puts the music in it. The source is three royalty-free
# Audiodollar tracks kept in the repo at media/musiconhold — the Audiodollar source IDs are in the
# file names and in that directory's README — transcoded here to the one format this system
# stores, which format_wav plays with no transcoding at call time (D55, D117). Asterisk is built
# without format_mp3, so the MP3s themselves are not usable as they are.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MOH_DEFAULT_DIR="${MOH_DIR}/default"
MOH_SOURCE_DIR="${MOH_SOURCE_DIR:-${SCRIPT_DIR}/../../../media/musiconhold}"

# Which repo track becomes which file. The names are what the application's schema writes into
# MohFiles for the class that ships, so the two have to agree: default-N.g722, played in that
# order because the class sorts its directory alphabetically.
MOH_DEFAULT_TRACKS=(
  "default-1.g722:audiodollar-on-hold-music-371876.mp3"
  "default-2.g722:audiodollar-piano-piano-inspirational-music-570589.mp3"
  "default-3.g722:audiodollar-bollywood-bollywood-551817.mp3"
)

DRY_RUN=0
REBUILD=0

for arg in "$@"; do
  case "$arg" in
    --dry-run) DRY_RUN=1 ;;
    --rebuild) REBUILD=1 ;;
    *) echo "unknown option: ${arg}" >&2; exit 2 ;;
  esac
done

log()  { echo -e "\e[1;34m==>\e[0m $*"; }
warn() { echo -e "\e[1;33mWARN:\e[0m $*"; }
die()  { echo -e "\e[1;31mERROR:\e[0m $*" >&2; exit 1; }

# Every mutating action goes through run() or write_file(), so --dry-run prints the whole
# install as a list of commands without touching the machine.
run() {
  if (( DRY_RUN )); then
    echo "  [dry-run] $*"
    return 0
  fi
  "$@"
}

write_file() {
  local path="$1" owner="$2" mode="$3" content
  content=$(cat)
  if (( DRY_RUN )); then
    echo "  [dry-run] write ${path} (${owner}, ${mode}):"
    printf '%s\n' "$content" | while IFS= read -r line; do echo "  [dry-run]   | ${line}"; done
    return 0
  fi
  printf '%s\n' "$content" > "$path"
  chown "$owner" "$path"
  chmod "$mode" "$path"
}

# --- 1. preflight -------------------------------------------------------------

(( DRY_RUN )) && log "DRY RUN: nothing will be changed"

# A dry run only prints, so it does not need root — the point of it is to be reviewable.
if (( DRY_RUN )); then
  [[ $EUID -eq 0 ]] || warn "not root; fine for a dry run, required for a real install"
else
  [[ $EUID -eq 0 ]] || die "run as root"
fi
[[ -f /etc/debian_version ]] || die "this script targets Debian"

. /etc/os-release
case "${VERSION_ID:-}" in
  12|13) log "Debian ${VERSION_ID} (${VERSION_CODENAME})" ;;
  *)     warn "untested Debian version '${VERSION_ID:-unknown}', continuing" ;;
esac
log "architecture: $(uname -m)"

command -v systemctl >/dev/null || die "systemd is required"

# Parallel build jobs: roughly 1 GB RAM per job, or small cloud VMs OOM during compile.
MEM_GB=$(awk '/MemAvailable/ {print int($2/1024/1024)}' /proc/meminfo)
JOBS=$(nproc)
(( MEM_GB < JOBS )) && JOBS=$(( MEM_GB > 0 ? MEM_GB : 1 ))
(( MEM_GB < 2 )) && warn "only ${MEM_GB} GB RAM available; the build may fail without swap"
log "build jobs: ${JOBS} (${MEM_GB} GB available)"

# --- 2. clock -----------------------------------------------------------------
# D74: a TNPBX server's clock is UTC, full stop. The customer's local zone lives in the
# System.Timezone setting and every generated GotoIfTime names it, so the system clock never
# needs to be anything else. Set it before anything else so logs, CDRs and the build agree.

log "setting the clock to UTC with NTP"
if command -v timedatectl >/dev/null; then
  run timedatectl set-timezone Etc/UTC
  run timedatectl set-ntp true
else
  run ln -sf /usr/share/zoneinfo/Etc/UTC /etc/localtime
  write_file /etc/timezone root:root 0644 <<<"Etc/UTC"
fi
log "system clock: $(date -u)"

# --- 3. packages --------------------------------------------------------------
# Build dependencies for Asterisk, plus what the application needs at run time: libicu for
# .NET globalisation, ffmpeg for announcement conversion (D55) and for the music on hold this
# script transcodes and the upload form converts (D122), sqlite3 for looking at the database by
# hand, curl and ca-certificates for outbound HTTPS, python3 for the voicemail mailcmd script
# (D126) — a Debian standard install has it, but a minimal cloud image may not.

case "${VERSION_ID:-13}" in
  12) ICU_PACKAGE="libicu72" ;;
  *)  ICU_PACKAGE="libicu76" ;;
esac

log "installing packages (icu: ${ICU_PACKAGE})"
export DEBIAN_FRONTEND=noninteractive
run apt-get update -q
run apt-get install -y -q --no-install-recommends \
  build-essential pkg-config ca-certificates wget curl bzip2 patch openssl procps iproute2 \
  libedit-dev libjansson-dev libxml2-dev uuid-dev libsqlite3-dev libssl-dev \
  libncurses-dev libsrtp2-dev \
  ffmpeg sqlite3 python3 "${ICU_PACKAGE}"

# --- 4. users, groups and directories ----------------------------------------
# The layout proven on the lab VM (D94). The web process never runs as root and never uses
# sudo; it writes generated config because it is in the asterisk group and /etc/asterisk is
# group-writable and setgid (D18).

if id asterisk &>/dev/null; then
  log "asterisk user exists"
else
  log "creating the asterisk system user"
  run useradd --system --user-group --home-dir /var/lib/asterisk --shell /usr/sbin/nologin asterisk
fi

if id "$APP_USER" &>/dev/null; then
  log "${APP_USER} user exists"
else
  log "creating the ${APP_USER} system user (the web application's account)"
  run useradd --system --user-group --home-dir "$APP_HOME" --shell /usr/sbin/nologin "$APP_USER"
fi

if id -nG "$APP_USER" 2>/dev/null | tr ' ' '\n' | grep -qx asterisk; then
  log "${APP_USER} is already in the asterisk group"
else
  log "adding ${APP_USER} to the asterisk group"
  run usermod -aG asterisk "$APP_USER"
fi

log "creating directories"
run mkdir -p /etc/asterisk /var/lib/asterisk /var/log/asterisk /var/spool/asterisk \
             "$ANNOUNCEMENTS_DIR" "$MOH_DIR" "$MOH_DEFAULT_DIR" \
             "$APP_HOME" "$APP_BIN_DIR" "$APP_CONFIG_DIR" "$WHISPER_DIR"

apply_layout() {
  run chown -R asterisk:asterisk /var/lib/asterisk /var/log/asterisk /var/spool/asterisk
  run chmod 0755 /var/lib/asterisk /var/log/asterisk /var/spool/asterisk

  # /etc/asterisk: setgid so that every file the web user creates here is group-owned by
  # asterisk and therefore readable by the asterisk process (D18). Verified on the lab VM as
  # drwxrws--- root asterisk.
  run chown root:asterisk /etc/asterisk
  run chmod 2770 /etc/asterisk

  # Announcement audio (converted uploads) lands here; same setgid model, so the files the web
  # user writes stay group-readable by asterisk (D56).
  run chown -R asterisk:asterisk /var/lib/asterisk/sounds/tnpbx
  run chmod 2770 /var/lib/asterisk/sounds/tnpbx "$ANNOUNCEMENTS_DIR"

  # Music on hold lands here, one directory per class: the generated musiconhold.conf names
  # <this>/<class directory> (D119, D122). Same setgid model again, and on the subdirectories
  # too — the web user creates a class directory itself when a class is added, and the setgid bit
  # on the parent is what makes what it writes there group-owned by asterisk.
  run chown -R asterisk:asterisk "$MOH_DIR"
  run chmod 2770 "$MOH_DIR" "$MOH_DEFAULT_DIR"

  # The application's deploy target. Created without the application in it: the app is deployed
  # separately. The two subdirectories below are not the app's code, which is why they are here.
  run chown "${APP_USER}:asterisk" "$APP_HOME"
  run chmod 0750 "$APP_HOME"

  # The voicemail mailcmd script lives here (D126). root:root 0755 on purpose: Asterisk executes
  # it as the asterisk user, and neither that user nor the web user may rewrite what it runs.
  # Readable and traversable by everyone, which costs nothing — the secret is in Config, not here.
  run chown root:root "$APP_BIN_DIR"
  run chmod 0755 "$APP_BIN_DIR"

  # mail.json: written by the web application, read by that script as the asterisk user. Setgid
  # so what tnpbx writes here is group-owned by asterisk (the same trick as /etc/asterisk, D18),
  # and no world bit at all, because the file carries the SMTP password.
  run chown "${APP_USER}:asterisk" "$APP_CONFIG_DIR"
  run chmod 2750 "$APP_CONFIG_DIR"

  # The speech model the mailcmd script feeds to whisper-cli (D128). root-owned and world-readable
  # for the same reasons bin/ is: the asterisk process reads it, nothing else may rewrite it, and
  # there is no secret in a language model. Empty unless section 6 managed to download one.
  run chown root:root "$WHISPER_DIR"
  run chmod 0755 "$WHISPER_DIR"
}
apply_layout

# The one program Asterisk runs that we wrote (D126): app_voicemail hands it the composed email
# on stdin and it relays it through the SMTP settings the application stores. Installed root-owned
# so that a compromised web or asterisk process cannot turn mailcmd into something else.
if [[ -f "${SCRIPT_DIR}/voicemail-mail" ]]; then
  log "installing the voicemail mail relay into ${APP_BIN_DIR}"
  run install -o root -g root -m 0755 "${SCRIPT_DIR}/voicemail-mail" "${APP_BIN_DIR}/voicemail-mail"
else
  warn "voicemail-mail is not beside this script; voicemail to email will not work until it is installed"
fi

# --- 5. Asterisk --------------------------------------------------------------

installed_version() {
  [[ -x /usr/sbin/asterisk ]] || return 1
  /usr/sbin/asterisk -V 2>/dev/null | awk '{print $2}'
}

CURRENT_VERSION=$(installed_version || true)

if [[ -n "$CURRENT_VERSION" && "$CURRENT_VERSION" == "${ASTERISK_MAJOR}."* && $REBUILD -eq 0 ]]; then
  log "Asterisk ${CURRENT_VERSION} already installed, skipping the build (use --rebuild to force)"
else
  [[ -n "$CURRENT_VERSION" ]] && log "installed Asterisk is ${CURRENT_VERSION}, wanted ${ASTERISK_MAJOR}.x"

  log "downloading ${TARBALL}"
  run rm -rf "$BUILD_DIR"
  run mkdir -p "$BUILD_DIR"
  if (( DRY_RUN )); then
    echo "  [dry-run] cd /usr/src && wget -q -O ${TARBALL} ${DOWNLOAD_BASE}/${TARBALL}"
    echo "  [dry-run] verify sha256 against ${DOWNLOAD_BASE}/asterisk-${ASTERISK_MAJOR}-current.sha256"
    echo "  [dry-run] tar -xzf ${TARBALL} -C ${BUILD_DIR} --strip-components=1"
  else
    cd /usr/src
    wget -q -O "$TARBALL" "${DOWNLOAD_BASE}/${TARBALL}"

    # Best effort integrity check; not every "-current" link has a published checksum.
    if wget -q -O "${TARBALL}.sha256" "${DOWNLOAD_BASE}/asterisk-${ASTERISK_MAJOR}-current.sha256"; then
      expected=$(awk '{print $1}' "${TARBALL}.sha256")
      actual=$(sha256sum "$TARBALL" | awk '{print $1}')
      [[ "$expected" == "$actual" ]] || die "sha256 mismatch for ${TARBALL}"
      log "sha256 verified"
    else
      warn "no sha256 published for ${TARBALL}; relying on HTTPS only"
    fi

    tar -xzf "$TARBALL" -C "$BUILD_DIR" --strip-components=1
  fi

  log "configuring (bundled pjproject)"
  if (( DRY_RUN )); then
    echo "  [dry-run] cd ${BUILD_DIR} && ./configure --with-pjproject-bundled"
    echo "  [dry-run] make menuselect.makeopts && menuselect/menuselect --disable BUILD_NATIVE menuselect.makeopts"
    echo "  [dry-run] make -j${JOBS}"
    echo "  [dry-run] make install    (binaries, core sounds; samples deliberately NOT installed)"
    echo "  [dry-run] fetch + extract G.722 core sounds into /var/lib/asterisk/sounds/en (D117)"
    echo "  [dry-run] ldconfig"
    # The music on hold that ships is installed below, whether or not Asterisk was rebuilt.
  else
    cd "$BUILD_DIR"
    ./configure --with-pjproject-bundled > /tmp/asterisk-configure.log 2>&1 \
      || die "configure failed, see /tmp/asterisk-configure.log"

    # BUILD_NATIVE compiles for this exact CPU; disable it so the binary survives a host change.
    # The module set is the stock one: what actually gets *loaded* is the generated modules.conf
    # allowlist (D31), so trimming the build as well would only mean rebuilding whenever a
    # feature needs a module.
    make menuselect.makeopts > /dev/null
    menuselect/menuselect --disable BUILD_NATIVE menuselect.makeopts

    log "compiling with ${JOBS} jobs (this takes a while)"
    make -j"${JOBS}" > /tmp/asterisk-build.log 2>&1 \
      || die "build failed, see /tmp/asterisk-build.log"

    # "make install" only: binaries, libraries, modules and the core sound files the generated
    # dialplan plays (ss-noservice, demo-echotest, invalid). "make samples" is deliberately not
    # run — the database is the source of truth and the application generates every file in
    # /etc/asterisk on its first apply (D4, D92), so sample config would be unmanaged files that
    # also happen to make a not-yet-configured Asterisk startable with autoload = yes.
    log "installing"
    make install > /tmp/asterisk-install.log 2>&1 \
      || die "install failed, see /tmp/asterisk-install.log"
    ldconfig

    # make install ships the core prompts in GSM only. The G.722 set (D117) is fetched from the
    # official releases so wideband prompts are not transcoded up from GSM. The tarball is flat,
    # so it extracts straight into the language directory alongside the GSM copies; the licenses
    # and CHANGES that ship in it belong there too.
    log "installing G.722 core sounds"
    run curl -sSfL -o /tmp/core-sounds-g722.tar.gz \
      "${CORE_SOUNDS_URL}/asterisk-core-sounds-en-g722-${CORE_SOUNDS_VERSION}.tar.gz"
    run tar -xzf /tmp/core-sounds-g722.tar.gz -C /var/lib/asterisk/sounds/en
    run rm -f /tmp/core-sounds-g722.tar.gz
  fi

  # make install creates its own directories; re-assert the layout over the top of them.
  log "re-applying ownership and permissions after make install"
  apply_layout
fi

# The music on hold that ships with the product (D122), outside the build branch above: an
# upgrade that skips the Asterisk rebuild still has to end up with the music the application
# lists. The application's schema creates the Default class and a row per track; this puts the
# audio where that class plays it. Nothing is overwritten — a track an admin has replaced through
# the UI stays replaced.
install_default_moh() {
  # D119 kept every track straight in ${MOH_DIR}, because there was one class. Anything still
  # loose there belongs to the class that ships, which is where those rows now point.
  if compgen -G "${MOH_DIR}/*.wav" > /dev/null; then
    log "moving music on hold into ${MOH_DEFAULT_DIR} (D122)"
    run mv -n "${MOH_DIR}"/*.wav "${MOH_DEFAULT_DIR}/"
  fi

  if [[ ! -d "$MOH_SOURCE_DIR" ]]; then
    warn "no music on hold source at ${MOH_SOURCE_DIR}; the Default class stays empty until tracks are uploaded"
    return 0
  fi

  # ffmpeg was installed in step 3, so its absence means something went wrong rather than
  # something is missing. Not checked during a --dry-run review, which may be run anywhere.
  if (( ! DRY_RUN )); then
    command -v ffmpeg &>/dev/null || die "ffmpeg is not installed, so the music on hold cannot be converted"
  fi

  log "installing the music on hold that ships (from ${MOH_SOURCE_DIR})"
  for track in "${MOH_DEFAULT_TRACKS[@]}"; do
    local target="${MOH_DEFAULT_DIR}/${track%%:*}"
    local mp3="${MOH_SOURCE_DIR}/${track#*:}"

    # Already there: either this has run before, or the admin replaced the track through the UI.
    [[ -f "$target" ]] && continue
    [[ -f "$mp3" ]] || { warn "missing ${mp3}, skipping $(basename "$target")"; continue; }

    # 16-bit 8 kHz mono PCM WAV: the same conversion the upload form does, and what format_wav
    # plays with no transcoding at call time (D55).
    run ffmpeg -nostdin -loglevel error -y -i "$mp3" -ar 16000 -ac 1 -acodec g722 "$target"

    # Owned by the web user, group asterisk: Asterisk reads it through the group (D18), and the
    # application renames this very file when an admin renames the track — which needs to own it.
    run chown "${APP_USER}:asterisk" "$target"
    run chmod 0640 "$target"
  done
}
install_default_moh

# --- 6. speech recognition, and it is optional --------------------------------
#
# whisper.cpp, which is what transcribes a voicemail into the email it is attached to (D128).
# Every step here is best effort and **none of them may end the install**: a box without it is a
# supported box that emails voicemail exactly as it always did, and the mailcmd script checks for
# the binary and the model before it tries to use them. So each step warns and returns instead of
# dying, and this section is the only one in the file that does not use die().
#
# Built from source like Asterisk, for the same reason: there is no Debian package, and the
# architecture this runs on (the lab is aarch64) is not one with a published binary. CPU inference
# only — no CUDA, no BLAS — because a PBX has spare CPU, and small.en is roughly real time on it,
# which is what lets the script transcribe in line before relaying the email.
#
# Not pinned to a release tag: whisper-cli's arguments have been stable for a long time, an
# optional component that fails to build costs nothing, and a tag that has been retired upstream
# would cost every new install its transcription. Worth revisiting if that ever stops being true.

install_whisper() {
  if (( DRY_RUN )); then
    echo "  [dry-run] apt-get install -y --no-install-recommends git cmake"
    echo "  [dry-run] git clone --depth 1 ${WHISPER_REPO} ${WHISPER_SOURCE_DIR}"
    echo "  [dry-run] cmake -B build -DCMAKE_BUILD_TYPE=Release && cmake --build build -j${JOBS}"
    echo "  [dry-run] install build/bin/whisper-cli ${APP_BIN_DIR}/whisper-cli (root:root 0755)"
    echo "  [dry-run] download ${WHISPER_MODEL_URL}"
    echo "  [dry-run]   to ${WHISPER_MODEL} (~488 MB)"
    echo "  [dry-run] every step above only warns on failure: transcription is optional"
    return 0
  fi

  if [[ -x "${APP_BIN_DIR}/whisper-cli" && -s "$WHISPER_MODEL" ]]; then
    log "voicemail transcription is already installed (delete ${APP_BIN_DIR}/whisper-cli to rebuild)"
    return 0
  fi

  log "installing voicemail transcription (optional; a failure here does not stop the install)"

  if ! apt-get install -y -q --no-install-recommends git cmake > /tmp/whisper-packages.log 2>&1; then
    warn "could not install git and cmake, so voicemail transcription will not be available (see /tmp/whisper-packages.log)"
    return 0
  fi

  if [[ ! -x "${APP_BIN_DIR}/whisper-cli" ]]; then
    rm -rf "$WHISPER_SOURCE_DIR"

    if ! git clone --depth 1 "$WHISPER_REPO" "$WHISPER_SOURCE_DIR" > /tmp/whisper-clone.log 2>&1; then
      warn "could not clone ${WHISPER_REPO}, so voicemail transcription will not be available (see /tmp/whisper-clone.log)"
      return 0
    fi

    log "compiling whisper.cpp with ${JOBS} jobs"
    if ! cmake -S "$WHISPER_SOURCE_DIR" -B "${WHISPER_SOURCE_DIR}/build" \
                -DCMAKE_BUILD_TYPE=Release > /tmp/whisper-build.log 2>&1 \
       || ! cmake --build "${WHISPER_SOURCE_DIR}/build" -j"${JOBS}" --config Release \
                >> /tmp/whisper-build.log 2>&1; then
      warn "the whisper.cpp build failed, so voicemail transcription will not be available (see /tmp/whisper-build.log)"
      return 0
    fi

    # The examples are built as well, and this is the only one of them we want: one binary, given
    # a model and a wav file, that prints the text. The rest stays in /usr/src.
    if [[ ! -x "${WHISPER_SOURCE_DIR}/build/bin/whisper-cli" ]]; then
      warn "the whisper.cpp build produced no whisper-cli, so voicemail transcription will not be available"
      return 0
    fi

    # root:root 0755, like the mailcmd script beside it: Asterisk runs this as the asterisk user,
    # and neither that user nor the web user may rewrite a program Asterisk executes.
    if ! install -o root -g root -m 0755 \
                 "${WHISPER_SOURCE_DIR}/build/bin/whisper-cli" "${APP_BIN_DIR}/whisper-cli"; then
      warn "could not install whisper-cli into ${APP_BIN_DIR}, so voicemail transcription will not be available"
      return 0
    fi
  fi

  if [[ ! -s "$WHISPER_MODEL" ]]; then
    log "downloading the whisper small.en model (~488 MB, this takes a while)"

    # Downloaded beside itself and renamed, so an interrupted download never looks like a model.
    if ! curl -sSfL -o "${WHISPER_MODEL}.part" "$WHISPER_MODEL_URL"; then
      rm -f "${WHISPER_MODEL}.part"
      warn "could not download the speech model, so voicemail transcription will not be available until ${WHISPER_MODEL} exists"
      return 0
    fi

    mv "${WHISPER_MODEL}.part" "$WHISPER_MODEL"
  fi

  # Both halves have to be there before this says so: the script needs the binary and the model,
  # and a summary that claims transcription works when it does not is worse than no summary.
  if [[ ! -x "${APP_BIN_DIR}/whisper-cli" || ! -s "$WHISPER_MODEL" ]]; then
    warn "voicemail transcription is not available: ${APP_BIN_DIR}/whisper-cli or ${WHISPER_MODEL} is missing"
    return 0
  fi

  chown root:root "${APP_BIN_DIR}/whisper-cli" "$WHISPER_MODEL"
  chmod 0755 "${APP_BIN_DIR}/whisper-cli"
  chmod 0644 "$WHISPER_MODEL"

  log "voicemail transcription is available: ${APP_BIN_DIR}/whisper-cli with small.en"
}

# Called with "|| warn" as well as being guarded step by step: "set -e" would otherwise turn an
# unguarded failure in there — a full disk during install, say — into a failed install, and this
# section is the one part of the script that is not allowed to do that.
install_whisper || warn "voicemail transcription could not be installed; nothing else on this box is affected"

# --- 7. systemd unit ----------------------------------------------------------
# Our own unit rather than "make config": Asterisk's init script runs it as root. This is the
# unit proven on the lab VM.

log "installing /etc/systemd/system/asterisk.service"
write_file /etc/systemd/system/asterisk.service root:root 0644 <<'EOF'
[Unit]
Description=Asterisk PBX
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=asterisk
Group=asterisk
RuntimeDirectory=asterisk
ExecStart=/usr/sbin/asterisk -f -q
ExecReload=/usr/sbin/asterisk -rx "core reload"
ExecStop=/usr/sbin/asterisk -rx "core stop gracefully"
Restart=on-failure
RestartSec=5
LimitNOFILE=65536

[Install]
WantedBy=multi-user.target
EOF

run systemctl daemon-reload
run systemctl enable asterisk

# Deliberately NOT started (D93). /etc/asterisk is empty until the application's first apply,
# and an Asterisk with no modules.conf autoloads everything — including the anonymous endpoint
# identifier. It starts for the first time after the app has written its config.
log "asterisk.service is enabled but NOT started (there is no configuration yet)"

# --- 8. summary ---------------------------------------------------------------

cat <<EOF

================================================================================
TNPBX base install complete$( (( DRY_RUN )) && echo " (DRY RUN — nothing was changed)")
================================================================================

Created:
  users        asterisk (system), ${APP_USER} (system, in the asterisk group)
  /etc/asterisk            root:asterisk  2770  setgid, empty
  /var/lib/asterisk        asterisk:asterisk  0755
  /var/spool/asterisk      asterisk:asterisk  0755
  /var/log/asterisk        asterisk:asterisk  0755
  ${ANNOUNCEMENTS_DIR}
                           asterisk:asterisk  2770  setgid
  ${MOH_DIR}     asterisk:asterisk  2770  setgid
  ${APP_HOME}               ${APP_USER}:asterisk  0750  the deploy target
  ${APP_BIN_DIR}           root:root  0755  voicemail-mail (Asterisk's mailcmd)
                                            and whisper-cli, if it built
  ${APP_CONFIG_DIR}        ${APP_USER}:asterisk  2750  setgid, holds mail.json
  ${WHISPER_DIR}        root:root  0755  the speech model, if it downloaded
  /usr/sbin/asterisk       Asterisk ${ASTERISK_MAJOR}.x built from source
  asterisk.service         our hardened unit, enabled

Clock: Etc/UTC with NTP (D74). Open hours are entered in the customer's zone.

Optional, and it says above whether it worked:
  * Voicemail transcription (whisper.cpp + the small.en model). If either the
    build or the 488 MB download failed, everything else on this box still
    works and voicemail is emailed without a transcript. Re-run this script to
    try again, or put whisper-cli and ${WHISPER_MODEL}
    in place by hand.

Deliberately NOT done:
  * The web application is NOT deployed and tnpbx-web.service does NOT exist.
    It is finalised and deployed separately, on top of what this script prepared.
  * Asterisk is enabled but STOPPED. /etc/asterisk is empty on purpose: the app
    generates every file there on its first apply. Starting Asterisk now would
    autoload every module it has, which is the opposite of what we want.
  * Asterisk sample configuration ("make samples") is NOT installed, for the
    same reason.
  * No polkit rule for restarting asterisk.service   (comes with the app deploy)
  * No Helper on its Unix socket                     (piece 19)
  * No firewall / nftables rules                     (piece 19)
  * No fail2ban and no AMI security-event blocker    (piece 20)

Next steps:
  1. Deploy the web application into ${APP_HOME} and install its unit.
  2. Sign in, set the AMI secret and the system timezone, add an extension.
  3. Apply config — that writes /etc/asterisk for the first time.
  4. systemctl start asterisk
  5. Until the firewall piece lands, restrict UDP 5060 and 10000-20000 to known
     addresses at the cloud firewall. An open 5060 is found by scanners in minutes.

EOF
