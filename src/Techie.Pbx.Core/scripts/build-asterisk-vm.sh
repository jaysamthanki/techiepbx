#!/usr/bin/env bash
#
# TNPBX spike: build Asterisk LTS from source on a fresh Debian VM and drop in a
# minimal hand-written config (two PJSIP extensions + echo test).
#
# This is a throwaway lab setup to learn what the installer will eventually automate.
# There is no firewall or fail2ban yet: on a cloud VM, restrict UDP 5060 and 10000-20000
# to your own IP at the cloud firewall (e.g. Azure NSG). SIP scanners find open 5060 in minutes.
#
# Usage (as root on Debian 12/13):
#   ./build-asterisk-vm.sh            # build + install + configure
#   ./build-asterisk-vm.sh --rebuild  # force a rebuild even if asterisk is installed
#
# After it finishes:
#   - Register two softphones to <vm-ip>:5060 (UDP) as 1001 and 1002 (passwords printed at the end)
#   - Dial 1002 from 1001, or 600 for the echo test
#   - Console: asterisk -rvvv

set -euo pipefail

ASTERISK_MAJOR="22"   # current LTS; bump when the next LTS ships
DOWNLOAD_BASE="https://downloads.asterisk.org/pub/telephony/asterisk"
TARBALL="asterisk-${ASTERISK_MAJOR}-current.tar.gz"
BUILD_DIR="/usr/src/asterisk-${ASTERISK_MAJOR}"
CRED_FILE="/root/tnpbx-spike-credentials.txt"

REBUILD=0
[[ "${1:-}" == "--rebuild" ]] && REBUILD=1

log()  { echo -e "\e[1;34m==>\e[0m $*"; }
warn() { echo -e "\e[1;33mWARN:\e[0m $*"; }
die()  { echo -e "\e[1;31mERROR:\e[0m $*" >&2; exit 1; }

# --- preflight ---------------------------------------------------------------

[[ $EUID -eq 0 ]] || die "run as root"
[[ -f /etc/debian_version ]] || die "this script targets Debian"

. /etc/os-release
case "${VERSION_ID:-}" in
  12|13) log "Debian ${VERSION_ID} (${VERSION_CODENAME})" ;;
  *)     warn "untested Debian version '${VERSION_ID:-unknown}', continuing" ;;
esac
log "architecture: $(uname -m)"

# Parallel build jobs: roughly 1 GB RAM per job, or small cloud VMs OOM during compile.
MEM_GB=$(awk '/MemAvailable/ {print int($2/1024/1024)}' /proc/meminfo)
JOBS=$(nproc)
(( MEM_GB < JOBS )) && JOBS=$(( MEM_GB > 0 ? MEM_GB : 1 ))
(( MEM_GB < 2 )) && warn "only ${MEM_GB} GB RAM available; the build may fail without swap"

# --- clock --------------------------------------------------------------------
# D74: a TNPBX server's clock is UTC, full stop. The customer's local zone lives
# in the System.Timezone setting and every generated GotoIfTime names it, so the
# system clock never needs to be anything else. Set it before anything else so
# logs, CDRs and the build all agree.
if command -v timedatectl &>/dev/null; then
  timedatectl set-timezone Etc/UTC
  timedatectl set-ntp true
else
  ln -sf /usr/share/zoneinfo/Etc/UTC /etc/localtime
  echo "Etc/UTC" > /etc/timezone
fi
log "system clock: UTC ($(date -u))"

# --- build dependencies ------------------------------------------------------

log "installing build dependencies"
export DEBIAN_FRONTEND=noninteractive
apt-get update -q
apt-get install -y -q --no-install-recommends \
  build-essential pkg-config ca-certificates wget bzip2 patch openssl procps iproute2 \
  libedit-dev libjansson-dev libxml2-dev uuid-dev libsqlite3-dev libssl-dev \
  libncurses-dev libsrtp2-dev \
  ffmpeg libicu76

# --- service account ---------------------------------------------------------

if ! id asterisk &>/dev/null; then
  log "creating asterisk system user"
  useradd --system --user-group --home-dir /var/lib/asterisk --shell /usr/sbin/nologin asterisk
fi

# --- download + build --------------------------------------------------------

if [[ -x /usr/sbin/asterisk && $REBUILD -eq 0 ]]; then
  log "asterisk already installed ($(/usr/sbin/asterisk -V)), skipping build (use --rebuild to force)"
else
  log "downloading ${TARBALL}"
  rm -rf "$BUILD_DIR"
  mkdir -p "$BUILD_DIR"
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
  cd "$BUILD_DIR"

  log "configuring (bundled pjproject)"
  ./configure --with-pjproject-bundled > /tmp/asterisk-configure.log 2>&1 \
    || die "configure failed, see /tmp/asterisk-configure.log"

  # BUILD_NATIVE compiles for this exact CPU; disable it so the binary survives VM host changes.
  make menuselect.makeopts > /dev/null
  menuselect/menuselect --disable BUILD_NATIVE menuselect.makeopts

  log "compiling with ${JOBS} jobs (this takes a while)"
  make -j"${JOBS}" > /tmp/asterisk-build.log 2>&1 \
    || die "build failed, see /tmp/asterisk-build.log"

  log "installing"
  make install > /tmp/asterisk-install.log 2>&1 \
    || die "install failed, see /tmp/asterisk-install.log"
  ldconfig

  # G.722 core prompts (D117): make install ships GSM only, and wideband prompts should not be
  # transcoded up from GSM. Flat tarball, extracted straight into the language directory.
  log "installing G.722 core sounds"
  curl -sSfL -o /tmp/core-sounds-g722.tar.gz \
    "https://downloads.asterisk.org/pub/telephony/sounds/releases/asterisk-core-sounds-en-g722-1.6.1.tar.gz"
  tar -xzf /tmp/core-sounds-g722.tar.gz -C /var/lib/asterisk/sounds/en
  rm -f /tmp/core-sounds-g722.tar.gz
fi

# --- music on hold ------------------------------------------------------------
# The class that ships with the product (D122): music on hold is one directory per class now, and
# this is the Default class's. The source is the three royalty-free Audiodollar tracks kept in the
# repo at media/musiconhold — their Audiodollar source IDs are in the file names and in that
# directory's README — transcoded here to 16-bit 8 kHz mono PCM WAV, the same conversion the
# upload form does and what format_wav plays with no work at call time (D55). Asterisk here is
# built without format_mp3, so the MP3s cannot be dropped in as they are.
#
# Outside the build branch above: a VM that already has Asterisk still wants the music.

MOH_DIR="/var/lib/asterisk/moh"
MOH_DEFAULT_DIR="${MOH_DIR}/default"
MOH_SOURCE_DIR="${MOH_SOURCE_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/../../../media/musiconhold}"

# default-N.g722, in the order the class plays them: it sorts its directory alphabetically. The
# names match what the application's schema writes into MohFiles for this class.
MOH_DEFAULT_TRACKS=(
  "default-1.g722:audiodollar-on-hold-music-371876.mp3"
  "default-2.g722:audiodollar-piano-piano-inspirational-music-570589.mp3"
  "default-3.g722:audiodollar-bollywood-bollywood-551817.mp3"
)

mkdir -p "$MOH_DEFAULT_DIR"

if [[ -d "$MOH_SOURCE_DIR" ]]; then
  log "installing music on hold from ${MOH_SOURCE_DIR}"
  for track in "${MOH_DEFAULT_TRACKS[@]}"; do
    target="${MOH_DEFAULT_DIR}/${track%%:*}"
    mp3="${MOH_SOURCE_DIR}/${track#*:}"

    [[ -f "$target" ]] && continue
    [[ -f "$mp3" ]] || { warn "missing ${mp3}, skipping $(basename "$target")"; continue; }

    ffmpeg -nostdin -loglevel error -y -i "$mp3" -ar 16000 -ac 1 -acodec g722 "$target"
  done
else
  warn "no music on hold source at ${MOH_SOURCE_DIR}; the Default class will be empty"
fi

# --- ownership ---------------------------------------------------------------

mkdir -p /etc/asterisk /var/lib/asterisk /var/log/asterisk /var/spool/asterisk
chown -R asterisk:asterisk /var/lib/asterisk /var/log/asterisk /var/spool/asterisk
# Music on hold: one directory per class (D122), setgid for the same reason the announcements
# directory is — the web user creates class directories and writes converted uploads into them.
chmod 2770 "$MOH_DIR" "$MOH_DEFAULT_DIR"
# The tracks that ship belong to the web user where there is one: the application renames the
# file when an admin renames the track, and chmod needs ownership. Asterisk reads via the group.
if id tnpbx &>/dev/null; then
  chown tnpbx:asterisk "${MOH_DEFAULT_DIR}"/*.wav 2>/dev/null || true
  chmod 0640 "${MOH_DEFAULT_DIR}"/*.wav 2>/dev/null || true
fi
# Announcement audio (converted uploads) lands here; setgid so app-written files stay
# group-readable by asterisk (D18, same model as /etc/asterisk).
mkdir -p /var/lib/asterisk/sounds/tnpbx/announcements
chown -R asterisk:asterisk /var/lib/asterisk/sounds/tnpbx
chmod 2770 /var/lib/asterisk/sounds/tnpbx /var/lib/asterisk/sounds/tnpbx/announcements
chown root:asterisk /etc/asterisk
# setgid (2770): the web user is in the asterisk group and writes generated config here, and
# every file it creates has to end up group-owned by asterisk so asterisk can read it (D18).
chmod 2770 /etc/asterisk

# --- minimal config ----------------------------------------------------------

if [[ -n "$(ls -A /etc/asterisk 2>/dev/null)" ]]; then
  backup="/etc/asterisk.bak.$(date +%Y%m%d%H%M%S)"
  log "backing up existing /etc/asterisk to ${backup}"
  cp -a /etc/asterisk "$backup"
  rm -f /etc/asterisk/*
fi

# --- network / NAT -----------------------------------------------------------
# Cloud VMs (Azure, AWS, ...) only see a private IP; the public IP is 1:1 NAT in front.
# PJSIP must advertise the public IP in SIP/SDP or calls connect with no audio.
# Override detection with: PUBLIC_IP=x.x.x.x ./build-asterisk-vm.sh

LOCAL_IP=$(ip -4 route get 1.1.1.1 | awk '{for (i = 1; i < NF; i++) if ($i == "src") print $(i + 1)}')
LOCAL_NET=$(ip -4 route show scope link | awk -v ip="$LOCAL_IP" '$0 ~ ("src " ip "( |$)") {print $1; exit}')

is_ipv4() { [[ "$1" =~ ^[0-9]{1,3}(\.[0-9]{1,3}){3}$ ]]; }

if [[ -z "${PUBLIC_IP:-}" ]]; then
  # Azure IMDS (only populated for some public IP SKUs), then a public echo service.
  PUBLIC_IP=$(wget -q -T 3 -t 1 -O- --header="Metadata: true" \
    "http://169.254.169.254/metadata/instance/network/interface/0/ipv4/ipAddress/0/publicIpAddress?api-version=2021-02-01&format=text" 2>/dev/null || true)
  is_ipv4 "$PUBLIC_IP" || PUBLIC_IP=$(wget -q -T 5 -t 1 -O- https://api.ipify.org 2>/dev/null || true)
fi
is_ipv4 "${PUBLIC_IP:-}" || { warn "could not detect public IP, assuming no NAT"; PUBLIC_IP="$LOCAL_IP"; }

log "local ${LOCAL_IP} (${LOCAL_NET:-unknown net}), public ${PUBLIC_IP}"

NAT_CONFIG=""
if [[ "$PUBLIC_IP" != "$LOCAL_IP" && -n "$LOCAL_NET" ]]; then
  NAT_CONFIG="local_net = ${LOCAL_NET}
external_media_address = ${PUBLIC_IP}
external_signaling_address = ${PUBLIC_IP}"
fi

gen_secret() { openssl rand -base64 18 | tr -d '/+=' | cut -c1-20; }
PW_1001=$(gen_secret)
PW_1002=$(gen_secret)
AMI_SECRET=$(gen_secret)

log "writing minimal config"

cat > /etc/asterisk/asterisk.conf <<'EOF'
; Directories use compiled-in defaults.
[options]
verbose = 3
EOF

cat > /etc/asterisk/modules.conf <<'EOF'
[modules]
autoload = yes
; Things we will never use. Later the app generates an explicit allowlist (autoload = no).
noload = chan_iax2.so
noload = chan_unistim.so
noload = chan_motif.so
noload = res_xmpp.so
noload = pbx_dundi.so
noload = res_phoneprov.so
noload = res_pjsip_phoneprov_provider.so
EOF

cat > /etc/asterisk/logger.conf <<'EOF'
[general]
dateformat = %F %T.%3q

[logfiles]
console => notice,warning,error
messages.log => notice,warning,error,security
; Security events (failed auth etc.) - input for fail2ban / our own blocker later.
security.log => security
EOF

cat > /etc/asterisk/rtp.conf <<'EOF'
[general]
rtpstart = 10000
rtpend = 20000
EOF

cat > /etc/asterisk/manager.conf <<EOF
[general]
enabled = yes
port = 5038
bindaddr = 127.0.0.1

[tnpbx]
secret = ${AMI_SECRET}
deny = 0.0.0.0/0.0.0.0
permit = 127.0.0.1/255.255.255.255
read = system,call,log,verbose,agent,user,config,dtmf,reporting,cdr,dialplan,security
write = system,call,agent,user,config,command,reporting,originate
EOF

pjsip_extension() {
  local ext="$1" pw="$2"
  cat <<EOF

[${ext}]
type = endpoint
context = internal
disallow = all
allow = ulaw,alaw
auth = ${ext}-auth
aors = ${ext}
direct_media = no
rtp_symmetric = yes
force_rport = yes
rewrite_contact = yes

[${ext}-auth]
type = auth
auth_type = digest
username = ${ext}
password = ${pw}

[${ext}]
type = aor
max_contacts = 1
remove_existing = yes
EOF
}

{
  cat <<EOF
[transport-udp]
type = transport
protocol = udp
bind = 0.0.0.0:5060
${NAT_CONFIG}
EOF
  pjsip_extension 1001 "$PW_1001"
  pjsip_extension 1002 "$PW_1002"
} > /etc/asterisk/pjsip.conf

cat > /etc/asterisk/extensions.conf <<'EOF'
[internal]
; Extension to extension
exten => _1XXX,1,Dial(PJSIP/${EXTEN},30)
 same => n,Hangup()

; Echo test
exten => 600,1,Answer()
 same => n,Playback(demo-echotest)
 same => n,Echo()
 same => n,Hangup()
EOF

chown root:asterisk /etc/asterisk/*
chmod 0640 /etc/asterisk/*

# --- systemd -----------------------------------------------------------------

log "installing systemd unit"
cat > /etc/systemd/system/asterisk.service <<'EOF'
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

systemctl daemon-reload
systemctl enable asterisk > /dev/null
systemctl restart asterisk

log "waiting for asterisk to come up"
for _ in $(seq 1 30); do
  if /usr/sbin/asterisk -rx "core show version" &>/dev/null; then break; fi
  sleep 1
done
/usr/sbin/asterisk -rx "core show version" || die "asterisk did not start; check: journalctl -u asterisk"

# --- summary -----------------------------------------------------------------

umask 077
cat > "$CRED_FILE" <<EOF
TNPBX spike credentials ($(date -Is))
SIP server : ${PUBLIC_IP}:5060 (UDP)
1001       : ${PW_1001}
1002       : ${PW_1002}
AMI        : 127.0.0.1:5038 user tnpbx secret ${AMI_SECRET}
EOF

log "done"
cat "$CRED_FILE"
echo
echo "Saved to ${CRED_FILE}. Useful commands:"
echo "  asterisk -rvvv"
echo "  asterisk -rx 'pjsip show endpoints'"
echo "  tail -f /var/log/asterisk/security.log"
