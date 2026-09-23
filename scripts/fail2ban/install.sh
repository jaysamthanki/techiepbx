#!/bin/bash
# Piece 20: install fail2ban for TNPBX (D7: fail2ban first, own blocker later).
#
# Run as root on the PBX server. Debian; fail2ban 1.1 uses nftables by default.
# The filter and jail live beside this script in the repo and are copied into
# /etc/fail2ban, so the server's copy always matches the repo.

set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"

if [[ $EUID -ne 0 ]]; then
    echo "Run as root (this installs packages and writes /etc/fail2ban)." >&2
    exit 1
fi

export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y -qq fail2ban nftables

install -m 0644 "$here/filter-tnpbx.conf" /etc/fail2ban/filter.d/tnpbx.conf
install -m 0644 "$here/jail-tnpbx.local"  /etc/fail2ban/jail.d/tnpbx.local

systemctl enable --now fail2ban
systemctl restart fail2ban

echo "fail2ban installed; verify with:"
echo "  fail2ban-client status tnpbx"
echo "  fail2ban-regex /var/log/asterisk/security.log /etc/fail2ban/filter.d/tnpbx.conf"