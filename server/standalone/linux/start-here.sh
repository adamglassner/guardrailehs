#!/usr/bin/env bash
# Runs GuardHawk Server in this terminal (Ctrl+C stops it). Data goes in ./data next to this script.
cd "$(dirname "$0")"
chmod +x ./GuardrailServer
exec ./GuardrailServer "$@"
