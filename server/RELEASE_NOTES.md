**0.1.1:** Guardrail Server is now **GuardHawk Server**, part of the Bytehawk "Hawk" family. Updating keeps your data, accounts and settings exactly where they were (C:\ProgramData\Guardrail Server on Windows, /var/lib/guardrail-server on Linux, the same Docker image and volume), and every version of the desktop app still connects.

GuardHawk Server: the optional, self-hosted sync server for GuardHawk. GuardHawk keeps working without it; connect when you want a whole team on the same data.

**Download**
- **Windows:** `GuardHawkServer-<version>-Setup.exe` installs a Windows service and opens the web panel. Uninstall it from Settings → Apps.
- **Windows (no install):** `GuardHawkServer-<version>-win-x64.zip`. Double-click `Start-GuardHawk-Server.cmd`.
- **Linux:** `GuardHawkServer-<version>-linux-x64.tar.gz` (or `linux-arm64` for a Raspberry Pi). Install with `sudo ./install.sh`, remove with `sudo ./uninstall.sh`.
- **Docker:** `docker run -d -p 5080:5080 -v guardrail-data:/data ghcr.io/adamglassner/guardrail-server`

Then open `http://<server>:5080`, create the admin account, and connect GuardHawk under **Settings → GuardHawk Server**.

**Running more than one server:** switch to PostgreSQL and S3-compatible storage. `docker-compose.scale.yml` and `deploy/kubernetes` in the repository have ready-made setups. See server/README.md.
