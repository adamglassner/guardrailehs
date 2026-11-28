#!/usr/bin/env bash
# Removes the GuardHawk Server service:  sudo ./uninstall.sh
# Your data in /var/lib/guardrail-server is kept unless you add --purge.
set -euo pipefail
if [ "$(id -u)" -ne 0 ]; then echo "Run this with sudo: sudo ./uninstall.sh"; exit 1; fi
systemctl disable --now guardrail-server 2>/dev/null || true
rm -f /etc/systemd/system/guardrail-server.service
systemctl daemon-reload
rm -rf /opt/guardrail-server
if [ "${1:-}" = "--purge" ]; then
  rm -rf /var/lib/guardrail-server
  userdel guardrail 2>/dev/null || true
  echo "GuardHawk Server and all of its data were removed."
else
  echo "GuardHawk Server removed. Data kept in /var/lib/guardrail-server (run with --purge to delete it)."
fi
