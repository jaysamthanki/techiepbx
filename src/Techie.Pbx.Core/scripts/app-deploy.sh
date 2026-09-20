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
#   - preserves an existing appsettings.json and Data/ (the database) across
#     re-deploys: config and the SQLite database live in the target, not the
#     tarball (D95)
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
MOH_DIR="/var/lib/asterisk/moh"
UNIT="/etc/systemd/system/tnpbx-web.service"
POLKIT="/etc/polkit-1/rules.d/40-tnpbx-asterisk.rules"

log() { echo -e "\e[1;34m==>\e[0m $*"; }
die() { echo -e "\e[1;31mERROR:\e[0m $*" >&2; exit 1; }

[[ $EUID -eq 0 ]] || die "run as root"
id tnpbx &>/dev/null || die "tnpbx user missing — run install.sh (part 1) first"
[[ -d /opt/tnpbx ]] || die "/opt/tnpbx missing — run install.sh (part 1) first"

TARBALL="${1:-}"
[[ -n "$TARBALL" && -f "$TARBALL" ]] || die "usage: app-deploy.sh <tnpbx-web.tgz> (the self-contained linux publish)"

# --- unpack, preserving server-side state -------------------------------------

log "deploying ${TARBALL} into ${APP_HOME}"
mkdir -p "${APP_HOME}/Data"
# The tarball carries repo defaults; the server's own appsettings.json and
# database (Data/) are the live state and must survive every re-deploy.
if [[ -f "${APP_HOME}/appsettings.json" ]]; then
    cp "${APP_HOME}/appsettings.json" /tmp/tnpbx-appsettings.keep
    KEEP_SETTINGS=1
fi
if [[ -d "${APP_HOME}/Data" ]]; then
    cp -a "${APP_HOME}/Data" /tmp/tnpbx-data.keep
    KEEP_DATA=1
fi

find "${APP_HOME}" -mindepth 1 -maxdepth 1 -exec rm -rf {} +
tar -xzf "$TARBALL" -C "$APP_HOME"

(( ${KEEP_SETTINGS:-0} )) && cp /tmp/tnpbx-appsettings.keep "${APP_HOME}/appsettings.json"
(( ${KEEP_DATA:-0} )) && rm -rf "${APP_HOME}/Data" && cp -a /tmp/tnpbx-data.keep "${APP_HOME}/Data"
rm -rf /tmp/tnpbx-data.keep /tmp/tnpbx-appsettings.keep

chown -R "${APP_USER}:${APP_GROUP}" "$APP_HOME"
chmod 0750 "$APP_HOME"
[[ -f "${APP_HOME}/Techie.Pbx.Web" ]] || die "tarball did not contain Techie.Pbx.Web — is this the self-contained publish?"

# --- music on hold directory ---------------------------------------------------
#
# The one directory res_musiconhold plays for the generated hold class (D119). The
# same setgid model as the announcements directory: owned by asterisk, group
# writable, so the files tnpbx uploads are group-readable by the asterisk process.
# Created here as well as in install.sh, because an existing box was installed
# before this directory existed.

log "creating ${MOH_DIR}"
mkdir -p "$MOH_DIR"
chown asterisk:asterisk "$MOH_DIR"
chmod 2770 "$MOH_DIR"

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
