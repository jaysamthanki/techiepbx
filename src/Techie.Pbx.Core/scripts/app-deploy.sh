#!/usr/bin/env bash
#
# TNPBX installer, part 2: deploy the web application onto a box prepared by
# install.sh (D92). Everything this script needs was created by part 1: the
# tnpbx user, the asterisk group membership, /opt/tnpbx, the runtime packages.
#
# Usage (as root on the prepared server, with the publish tarball in hand):
#   ./app-deploy.sh /path/to/tnpbx-web.tgz
#
# What it does:
#   - unpacks the self-contained publish into /opt/tnpbx (tnpbx:asterisk 0750)
#   - preserves an existing appsettings.json, Data/ (the database), bin/ (the
#     root-owned voicemail mailcmd script and whisper-cli), Config/ (mail.json,
#     the SMTP credentials that script reads) and whisper/ (the speech model)
#     across re-deploys: all of it lives in the target, not the tarball
#     (D95, D126, D128)
#   - (re)installs bin/voicemail-mail root:root 0755 from the tarball (D126)
#   - writes /etc/systemd/system/tnpbx-web.service (User=tnpbx, never root,
#     bindings the app chooses itself: 8080 always, plus 80 always and 443 once
#     a certificate exists (D99). The unit grants CAP_NET_BIND_SERVICE and
#     nothing else, which is how an unprivileged user binds ports 80 and 443
#     (D95, D99).
#   - writes the polkit rule that lets tnpbx restart exactly asterisk.service
#     and nothing else (architecture.md; the app asks the operator, the Helper
#     or the admin to do the restart, never sudo)
#   - enables + starts tnpbx-web; does NOT start asterisk (part 1 left it
#     stopped until the app's first apply writes /etc/asterisk, D93)
#
# First run after this script: browse http://<host>:8080, sign in, set the AMI
# secret + timezone, add an extension, apply config, then
# `systemctl start asterisk` (as root, or as any admin via the polkit rule).

set -euo pipefail

APP_USER="tnpbx"
APP_GROUP="asterisk"
APP_HOME="/opt/tnpbx"
APP_BIN_DIR="${APP_HOME}/bin"
APP_CONFIG_DIR="${APP_HOME}/Config"
MOH_DIR="/var/lib/asterisk/moh"
WHISPER_DIR="${APP_HOME}/whisper"
UNIT="/etc/systemd/system/tnpbx-web.service"
POLKIT="/etc/polkit-1/rules.d/40-tnpbx-asterisk.rules"

log()  { echo -e "\e[1;34m==>\e[0m $*"; }
warn() { echo -e "\e[1;33mWARN:\e[0m $*" >&2; }
die()  { echo -e "\e[1;31mERROR:\e[0m $*" >&2; exit 1; }

[[ $EUID -eq 0 ]] || die "run as root"
id tnpbx &>/dev/null || die "tnpbx user missing — run install.sh (part 1) first"
[[ -d /opt/tnpbx ]] || die "/opt/tnpbx missing — run install.sh (part 1) first"

TARBALL="${1:-}"
[[ -n "$TARBALL" && -f "$TARBALL" ]] || die "usage: app-deploy.sh <tnpbx-web.tgz> (the self-contained linux publish)"

# --- unpack, preserving server-side state -------------------------------------

log "deploying ${TARBALL} into ${APP_HOME}"
mkdir -p "${APP_HOME}/Data"

# The tarball carries repo defaults; the server's own state must survive every re-deploy.
# appsettings.json and Data/ (the SQLite database and the data protection key ring) are that
# state, and so are the three directories install.sh creates: bin/ holds the root-owned mailcmd
# script Asterisk executes (and whisper-cli beside it), Config/ holds mail.json with the SMTP
# password in it (D126), and whisper/ holds a 488 MB speech model that a deploy has no way to
# fetch and no business re-downloading (D128).
#
# Written as plain "if" blocks rather than "test && command": under "set -e" a test that
# comes out false is a failed command at the top level, which would end the deploy.
KEEP_DIR=$(mktemp -d)
for kept in appsettings.json Data bin Config whisper; do
    if [[ -e "${APP_HOME}/${kept}" ]]; then
        cp -a "${APP_HOME}/${kept}" "${KEEP_DIR}/"
    fi
done

find "${APP_HOME}" -mindepth 1 -maxdepth 1 -exec rm -rf {} +
tar -xzf "$TARBALL" -C "$APP_HOME"

for kept in appsettings.json Data bin Config whisper; do
    if [[ -e "${KEEP_DIR}/${kept}" ]]; then
        rm -rf "${APP_HOME:?}/${kept}"
        cp -a "${KEEP_DIR}/${kept}" "${APP_HOME}/"
    fi
done
rm -rf "$KEEP_DIR"

chown -R "${APP_USER}:${APP_GROUP}" "$APP_HOME"
chmod 0750 "$APP_HOME"
[[ -f "${APP_HOME}/Techie.Pbx.Web" ]] || die "tarball did not contain Techie.Pbx.Web — is this the self-contained publish?"

# --- the voicemail mail relay ---------------------------------------------------
#
# app_voicemail's mailcmd (D126). Installed here as well as by install.sh, because a
# deploy is how a box that was installed before this existed gets it, and how one that
# has it gets a fixed version. It is put back root-owned AFTER the chown above: the web
# user must not be able to rewrite a program Asterisk executes.

mkdir -p "${APP_BIN_DIR}" "${APP_CONFIG_DIR}"

if [[ -f "${APP_HOME}/scripts/voicemail-mail" ]]; then
    log "installing the voicemail mail relay into ${APP_BIN_DIR}"
    install -o root -g root -m 0755 "${APP_HOME}/scripts/voicemail-mail" "${APP_BIN_DIR}/voicemail-mail"
elif [[ ! -f "${APP_BIN_DIR}/voicemail-mail" ]]; then
    warn "no voicemail-mail in the tarball and none installed; voicemail to email will not work"
fi

chown root:root "$APP_BIN_DIR"
chmod 0755 "$APP_BIN_DIR"
if [[ -f "${APP_BIN_DIR}/voicemail-mail" ]]; then
    chown root:root "${APP_BIN_DIR}/voicemail-mail"
    chmod 0755 "${APP_BIN_DIR}/voicemail-mail"
fi

# whisper-cli and its model are install.sh's business, not this script's — a 488 MB download is
# not part of a deploy (D128). They are only put back root-owned here, after the chown -R above,
# for the same reason the mailcmd script is: the web user may not rewrite a program that runs as
# the asterisk user, nor the model it is fed.
if [[ -f "${APP_BIN_DIR}/whisper-cli" ]]; then
    chown root:root "${APP_BIN_DIR}/whisper-cli"
    chmod 0755 "${APP_BIN_DIR}/whisper-cli"
fi

if [[ -d "$WHISPER_DIR" ]]; then
    chown -R root:root "$WHISPER_DIR"
    chmod 0755 "$WHISPER_DIR"
fi

# mail.json is written here by the app and read by that script as the asterisk user, so the
# directory is setgid asterisk and has no world bit — the file carries the SMTP password.
chown "${APP_USER}:${APP_GROUP}" "$APP_CONFIG_DIR"
chmod 2750 "$APP_CONFIG_DIR"

command -v python3 >/dev/null || warn "python3 is not installed, so voicemail cannot be emailed (apt-get install python3)"

# --- music on hold directory ---------------------------------------------------
#
# The base of what res_musiconhold plays: one directory per class inside it (D119,
# D122), and the class directories themselves are created by install.sh or by the
# app when a class is added. The same setgid model as the announcements directory:
# owned by asterisk, group writable, so the files tnpbx writes are group-readable by
# the asterisk process. Created here as well as in install.sh, because an existing
# box was installed before this directory existed.

log "creating ${MOH_DIR}"
mkdir -p "$MOH_DIR" "${MOH_DIR}/default"
chown asterisk:asterisk "$MOH_DIR" "${MOH_DIR}/default"
chmod 2770 "$MOH_DIR" "${MOH_DIR}/default"

# D119 kept every track loose in ${MOH_DIR}, because there was one class. The schema script this
# deploy brings points those rows at the class that ships, whose directory is "default" (D122),
# so the files have to follow them or the UI shows a table of missing audio.
if compgen -G "${MOH_DIR}/*.wav" > /dev/null; then
  log "moving music on hold into ${MOH_DIR}/default (D122)"
  mv -n "${MOH_DIR}"/*.wav "${MOH_DIR}/default/"
fi

# --- systemd unit --------------------------------------------------------------

log "writing tnpbx-web.service"
cat > "$UNIT" <<'EOF'
[Unit]
Description=TNPBX web application
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=tnpbx
Group=asterisk
WorkingDirectory=/opt/tnpbx
ExecStart=/opt/tnpbx/Techie.Pbx.Web
Restart=on-failure
RestartSec=5
# Hardening: the app has no business writing anywhere except its own home,
# /etc/asterisk (group-write via the setgid bit) and the sound directories.
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ReadWritePaths=/opt/tnpbx /etc/asterisk /var/lib/asterisk/sounds/tnpbx /var/lib/asterisk/moh /var/spool/asterisk
ProtectHome=true
LimitNOFILE=8192
# Ports 80 and 443 are privileged; the app runs as tnpbx, so it gets exactly
# the one capability that lets it bind them (D99).
AmbientCapabilities=CAP_NET_BIND_SERVICE
CapabilityBoundingSet=CAP_NET_BIND_SERVICE

[Install]
WantedBy=multi-user.target
EOF
systemctl daemon-reload
systemctl enable tnpbx-web

# --- polkit rule ---------------------------------------------------------------

log "writing polkit rule (tnpbx may restart asterisk.service, nothing else)"
mkdir -p /etc/polkit-1/rules.d
cat > "$POLKIT" <<'EOF'
// TNPBX: the tnpbx user may manage exactly one unit — asterisk.service —
// because Asterisk must restart when modules.conf/rtp.conf change (D33) and
// the web process never runs as root and never uses sudo (D3, D18).
polkit.addRule(function(action, subject) {
    if (action.id == "org.freedesktop.systemd1.manage-units" &&
        subject.user == "tnpbx" &&
        action.lookup("unit") == "asterisk.service" &&
        (action.lookup("verb") == "restart" || action.lookup("verb") == "start" || action.lookup("verb") == "stop")) {
        return polkit.Result.YES;
    }
});
EOF
chmod 0644 "$POLKIT"

# --- go ------------------------------------------------------------------------

log "starting tnpbx-web"
systemctl restart tnpbx-web
sleep 3
systemctl is-active tnpbx-web || die "tnpbx-web failed to start: journalctl -u tnpbx-web -n 50"

cat <<EOF

================================================================================
TNPBX web application deployed
================================================================================
  /opt/tnpbx            app + Data/tnpbx.db (SQLite, preserved on re-deploy)
  ${APP_BIN_DIR}        root:root 0755, voicemail-mail (Asterisk's mailcmd)
  ${APP_CONFIG_DIR}     ${APP_USER}:${APP_GROUP} 2750, mail.json (written by the app)
  ${WHISPER_DIR}    root:root 0755, the speech model, if install.sh got one
  ${MOH_DIR}   asterisk:asterisk 2770, the music on hold class's directory
  tnpbx-web.service     User=tnpbx, http://0.0.0.0:8080, hardened
  ${POLKIT}
                        tnpbx may start/stop/restart asterisk.service only

Next steps (first run):
  1. Browse http://<this-host>:8080 and sign in with Entra ID
  2. Settings: AMI secret, System.Timezone, Provisioning credentials
  3. Add an extension, then Apply config — that writes /etc/asterisk
  4. Start Asterisk:  sudo systemctl start asterisk
  5. Point phones at http://<host>:8080/polycom (option 160) or /yealink (66)

EOF
