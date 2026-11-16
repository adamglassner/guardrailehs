Guardrail Server: the optional, self-hosted sync server for Guardrail EHS. Guardrail keeps working without it; connect when you want a whole team on the same data.

**Download**
- **Windows:** `GuardrailServer-<version>-Setup.exe` installs a Windows service and opens the web panel. Uninstall it from Settings → Apps.
- **Windows (no install):** `GuardrailServer-<version>-win-x64.zip`. Double-click `Start-Guardrail-Server.cmd`.
- **Linux:** `GuardrailServer-<version>-linux-x64.tar.gz` (or `linux-arm64` for a Raspberry Pi). Install with `sudo ./install.sh`, remove with `sudo ./uninstall.sh`.
- **Docker:** `docker run -d -p 5080:5080 -v guardrail-data:/data ghcr.io/adamglassner/guardrail-server`

Then open `http://<server>:5080`, create the admin account, and connect Guardrail under **Settings → Guardrail Server**.

**Running more than one server:** switch to PostgreSQL and S3-compatible storage. `docker-compose.scale.yml` and `deploy/kubernetes` in the repository have ready-made setups. See server/README.md.
