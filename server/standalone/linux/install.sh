#!/usr/bin/env bash
# Installs GuardHawk Server as a systemd service:  sudo ./install.sh
# Program: /opt/guardrail-server   Data: /var/lib/guardrail-server   Port: 5080
# Uninstall later with:  sudo ./uninstall.sh   (or /opt/guardrail-server/uninstall.sh)
set -euo pipefail
if [ "$(id -u)" -ne 0 ]; then echo "Run this with sudo: sudo ./install.sh"; exit 1; fi
here="$(cd "$(dirname "$0")" && pwd)"

id guardrail >/dev/null 2>&1 || useradd --system --home /var/lib/guardrail-server --shell /usr/sbin/nologin guardrail
mkdir -p /opt/guardrail-server /var/lib/guardrail-server
systemctl stop guardrail-server 2>/dev/null || true
cp -r "$here/GuardrailServer" "$here/wwwroot" "$here/uninstall.sh" /opt/guardrail-server/
[ -f /opt/guardrail-server/appsettings.json ] || cp "$here/appsettings.json" /opt/guardrail-server/
chmod +x /opt/guardrail-server/GuardrailServer /opt/guardrail-server/uninstall.sh
chown -R guardrail:guardrail /var/lib/guardrail-server
cp "$here/guardrail-server.service" /etc/systemd/system/
systemctl daemon-reload
systemctl enable --now guardrail-server

ip=$(hostname -I 2>/dev/null | awk '{print $1}')
echo
echo "GuardHawk Server is running."
echo "  Web panel:  http://${ip:-localhost}:5080"
echo "  Status:     systemctl status guardrail-server"
echo "  Logs:       journalctl -u guardrail-server -f"
echo "  Uninstall:  sudo /opt/guardrail-server/uninstall.sh   (add --purge to delete the data too)"
echo "If a firewall is on, allow port 5080 (for example: sudo ufw allow 5080/tcp)."
