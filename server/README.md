# Guardrail Server

The optional, self-hosted server for Guardrail EHS. It puts a whole team on the same incidents, actions,
inspections, fleet records and documents, gives each person an account and a role, and comes with a web panel to
run it. Guardrail keeps working without it: everything stays on the PC until you connect, and each PC keeps a full
copy afterwards, so people can keep working when the server can't be reached.

- **Start small.** One program with a SQLite database on a Windows PC, a Linux box, a NAS or a Raspberry Pi.
- **Scale up without starting over.** Switch to PostgreSQL and S3-compatible storage and run as many copies as
  you need behind a load balancer. Sign-ins, the sign-in throttle and the pause switch are shared through the
  database, so it doesn't matter which copy answers. Ready-made setups for Docker Compose and Kubernetes are
  included.
- **Accounts and roles.** Admin, manager, member and viewer (read-only). Several organizations can share one
  server; each one only sees its own data.
- **Web panel.** Overview and live numbers, people, invites, the synced data (read-only), the servers in the
  cluster, settings, logs, backup and export. Pause syncing, restart or shut down from the browser.

## Install

All of them listen on port **5080**.

### Windows

Download **GuardrailServer-&lt;version&gt;-Setup.exe** from the
[latest server release](https://github.com/adamglassner/guardrailehs/releases?q=server-v) (or from **Servers** in
the Bytehawk Studio Launcher) and run it. It installs a Windows service that starts with Windows, can open the
firewall for your network, and opens the web panel. Data: `C:\ProgramData\Guardrail Server`.

**Uninstall:** Settings → Apps → Guardrail Server → Uninstall. It asks whether to delete the data too.

### Linux

Download **GuardrailServer-&lt;version&gt;-linux-x64.tar.gz** (or `linux-arm64` for a Raspberry Pi 4/5), unpack it and
run `sudo ./install.sh`. That installs `/opt/guardrail-server` as a systemd service with its data in
`/var/lib/guardrail-server`. To try it without installing, run `./start-here.sh`.

**Uninstall:** `sudo /opt/guardrail-server/uninstall.sh` (add `--purge` to delete the data too).

### Docker

```sh
docker run -d --name guardrail-server --restart on-failure \
  -p 5080:5080 -v guardrail-data:/data ghcr.io/adamglassner/guardrail-server:latest
```

or `docker compose up -d` with [docker-compose.yml](docker-compose.yml). The image is built for x64 and ARM64,
runs as a non-root user and has a health check.

**Uninstall:** `docker compose down` (add `-v` to delete the data volume), or `docker rm -f guardrail-server`.

### Windows without installing

Unzip **GuardrailServer-&lt;version&gt;-win-x64.zip** and double-click `Start-Guardrail-Server.cmd`. Data goes in
the `data` folder next to it. `Install-Service.ps1` turns it into a Windows service later and
`Uninstall-Service.ps1` removes it (`-Purge` deletes the data).

## First run

Open `http://<server>:5080`. The first visit asks for your organization's name and creates the **admin** account
for the web panel (separate from the accounts people use in Guardrail). Then:

1. **People → Add person** (they get a temporary password), or **Invites** to share a code, or turn on
   **Open sign-up** in Settings.
2. In Guardrail EHS: **Settings → Guardrail Server → Connect**, enter the server's address and sign in or join
   with the invite code. The first sync uploads what's on that PC; everyone else's PCs download it.

**Pause sync** stops syncing on every server (devices keep working and catch up later). **Restart** restarts the
server you're connected to. **Shut down** stops it until it's started again (as a Windows service, systemd service
or Docker container it stays stopped; Kubernetes always starts a new copy).

## Scaling up

| Size | Setup |
| --- | --- |
| One site, a few dozen people | The default: one server, SQLite, files in the data folder. |
| Hundreds of people, high availability | 2+ servers, PostgreSQL, S3-compatible storage (or one shared folder), a load balancer. [docker-compose.scale.yml](docker-compose.scale.yml) does this with Caddy, PostgreSQL and SeaweedFS (S3). |
| Many organizations, thousands of people | [deploy/kubernetes](deploy/kubernetes): 3 to 20 servers with autoscaling, rolling updates and a disruption budget, a managed PostgreSQL and object storage. |

How it stays correct with many servers:

- **Shared state lives in the database**: sessions, the sign-in throttle, the pause switch, settings and the
  cluster's heartbeat. Each server caches sessions for 30 seconds and settings for 5.
- **Sync order is per organization.** A push locks only its organization's row, so changes get sequence numbers
  in commit order ("changes after N" never misses one), and organizations never wait on each other.
- **Startup is safe.** Schema upgrades run under a PostgreSQL advisory lock, so servers can start together.
- **Graceful shutdown.** A stopping server fails `/readyz` first, so load balancers stop sending it requests
  while in-flight ones finish.
- **Watch it.** `/healthz` and `/readyz` for load balancers and Kubernetes, `/metrics` for Prometheus, and the web
  panel's **Servers** page lists every running copy with its traffic.

## Settings

Environment variables, or the same names without `GR_` under `"Guardrail"` in `appsettings.json`.

| Variable | What | Default |
| --- | --- | --- |
| `GR_PORT` | HTTP port | `5080` |
| `GR_DATA` | Data folder (SQLite database, files, logs) | `data` next to the program (`/data` in Docker) |
| `GR_DB` | `sqlite` or `postgres` | `sqlite` |
| `GR_DB_CONNECTION` | PostgreSQL connection string, e.g. `Host=db;Database=guardrail;Username=guardrail;Password=…` | |
| `GR_STORAGE` | `local` or `s3` | `local` |
| `GR_FILES` | Folder for files with `local` storage (a share every server can reach, to scale out) | `<data>/files` |
| `GR_S3_ENDPOINT` | S3 endpoint (MinIO, R2, B2, Wasabi…); leave empty for AWS | |
| `GR_S3_REGION`, `GR_S3_BUCKET` | Region and bucket (created on first start if missing) | `us-east-1` |
| `GR_S3_ACCESS_KEY`, `GR_S3_SECRET_KEY` | Credentials (or the usual AWS environment/role) | |
| `GR_S3_PATH_STYLE` | Path-style URLs (`true` for MinIO) | `true` |
| `GR_TRUST_PROXY` | Trust `X-Forwarded-*` from a load balancer or reverse proxy | `false` |
| `GR_HTTPS_PORT`, `GR_HTTPS_CERT`, `GR_HTTPS_CERT_PASSWORD` | Built-in HTTPS with a .pfx certificate | off |
| `GR_ADMIN_USERNAME`, `GR_ADMIN_PASSWORD` | Create the first panel admin without the setup page | |
| `GR_ORG_NAME` | Name of the first organization | `My organization` |
| `GR_NODE_NAME` | Name on the Servers page | machine name |
| `GR_METRICS_TOKEN` | Require `Authorization: Bearer <token>` on `/metrics` | open |
| `GR_RATE_LIMIT` | App API requests per minute per address, per server (0 = off) | `1200` |

## Security

- Data isn't end-to-end encrypted: the server stores it so roles, shared records and the web panel work. Run it
  on hardware you control and use **HTTPS** (a reverse proxy, a tunnel such as Tailscale or Cloudflare Tunnel, or
  the built-in HTTPS) when people connect from outside your network.
- Passwords are hashed with PBKDF2-SHA256 (600,000 rounds); sign-in tokens are stored as SHA-256 hashes.
- 10 wrong passwords for a name or address within 15 minutes locks it for 15 minutes (on every server).
- The web panel uses an HttpOnly, SameSite=Strict cookie and needs an `X-Guardrail` header on every change.
- **Forgotten passwords:** an admin resets them in **People** (a new temporary password, signed out everywhere).
  Forgot the panel admin password? Stop the server, start it once with `GR_ADMIN_USERNAME` and
  `GR_ADMIN_PASSWORD` set to a new admin, and sign in with that.

## Building

```sh
dotnet test server/Guardrail.Server.Tests
GR_TEST_POSTGRES="Host=localhost;Username=postgres;Password=…" dotnet test server/Guardrail.Server.Tests   # against PostgreSQL
dotnet run --project server/Guardrail.Server          # http://localhost:5080
docker build -f server/Dockerfile -t guardrail-server .
```

See [PROTOCOL.md](PROTOCOL.md) for the API. The [Server workflow](../.github/workflows/server.yml) runs the tests on
SQLite and PostgreSQL, builds the standalone packages, tests the Docker image alone and scaled out (3 servers,
PostgreSQL, S3 storage, load balancer), installs, restarts, shuts down and uninstalls the Windows service, takes
screenshots of the web panel, and publishes a `server-v<version>` release when the version in
`Guardrail.Server.csproj` changes on `main`.
