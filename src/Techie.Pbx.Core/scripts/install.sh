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
ANNOUNCEMENTS_DIR="/var/lib/asterisk/sounds/tnpbx/announcements"
MOH_DIR="/var/lib/asterisk/moh"

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
# .NET globalisation, ffmpeg for announcement conversion (D55), sqlite3 for looking at the
# database by hand, curl and ca-certificates for outbound HTTPS.

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
  ffmpeg sqlite3 "${ICU_PACKAGE}"

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
             "$ANNOUNCEMENTS_DIR" "$MOH_DIR" "$APP_HOME"

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

  # Music on hold (converted uploads) lands here, in the one directory the generated
  # musiconhold.conf names as its class's directory (D119). Same setgid model again.
  run chown -R asterisk:asterisk "$MOH_DIR"
  run chmod 2770 "$MOH_DIR"

  # The application's deploy target. Created and left EMPTY: the app is deployed separately.
  run chown "${APP_USER}:asterisk" "$APP_HOME"
  run chmod 0750 "$APP_HOME"
}
apply_layout

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

# --- 6. systemd unit ----------------------------------------------------------
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

# --- 7. summary ---------------------------------------------------------------

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
  ${APP_HOME}               ${APP_USER}:asterisk  0750  EMPTY, the deploy target
  /usr/sbin/asterisk       Asterisk ${ASTERISK_MAJOR}.x built from source
  asterisk.service         our hardened unit, enabled

Clock: Etc/UTC with NTP (D74). Open hours are entered in the customer's zone.

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
