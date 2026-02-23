# WattsOn Staging Deployment Guide

Deploy WattsOn to a Windows Server on the company LAN.

## Prerequisites

On the Windows Server (via RDP):

1. **Docker Desktop for Windows** — [download](https://www.docker.com/products/docker-desktop/)
   - During install: enable WSL2 backend
   - After install: open Docker Desktop, let it finish initializing
   - Verify: open PowerShell, run `docker --version`

2. **Git for Windows** — [download](https://git-scm.com/download/win)
   - Verify: `git --version`

## Deploy

### 1. Get the code

Open PowerShell on the server:

```powershell
cd C:\
git clone <your-repo-url> wattson
cd wattson
```

Or copy the folder from your dev machine over the network share.

### 2. Configure

Edit `.env.staging` — set a real database password:

```powershell
notepad .env.staging
```

Change `DB_PASSWORD=CHANGE_ME_use_a_real_password` to something real.

### 3. Start

```powershell
docker compose -f docker-compose.staging.yml --env-file .env.staging up -d --build
```

First build takes a few minutes (downloads .NET SDK, Node, Nginx images, builds everything). Subsequent starts are fast.

### 4. Verify

```powershell
docker compose -f docker-compose.staging.yml ps
```

All 4 services should show `Up (healthy)` after ~30 seconds.

Open a browser on any machine on the LAN and go to:

```
http://<server-ip>
```

You should see the WattsOn UI.

## Day-to-day Operations

### View logs

```powershell
# All services
docker compose -f docker-compose.staging.yml logs -f

# Single service
docker compose -f docker-compose.staging.yml logs -f api
```

### Stop

```powershell
docker compose -f docker-compose.staging.yml stop
```

### Restart

```powershell
docker compose -f docker-compose.staging.yml restart
```

### Update to latest code

```powershell
cd C:\wattson
git pull
docker compose -f docker-compose.staging.yml --env-file .env.staging up -d --build
```

### Reset database (wipe all data)

```powershell
docker compose -f docker-compose.staging.yml down -v
docker compose -f docker-compose.staging.yml --env-file .env.staging up -d --build
```

## Optional: Portainer (Web-based Docker Management)

Portainer gives you a web UI for managing containers — view logs, restart services, monitor resources — without needing to RDP in.

```powershell
docker compose -f docker-compose.staging.yml --profile management --env-file .env.staging up -d
```

Then open `https://<server-ip>:9443` from any browser on the LAN.
On first visit, create an admin account.

## Architecture

```
Browser (LAN) → :80 → Nginx (frontend container)
                         ├── Static files (React app)
                         └── /api/* → proxy → API container (:8080)
                                                └── PostgreSQL/TimescaleDB (:5432)
                       Worker container (background jobs)
                                └── PostgreSQL/TimescaleDB (:5432)
```

- **Nginx** serves the built React frontend and proxies `/api/*` to the .NET API
- **API** runs the .NET backend on port 8080 (not exposed externally)
- **Worker** runs background processing (DataHub polling, etc.)
- **DB** is TimescaleDB (PostgreSQL with time-series extensions)
- **Portainer** (optional) provides a management dashboard

## Ports

| Port | Service    | Notes                         |
|------|------------|-------------------------------|
| 80   | Frontend   | Main entry point              |
| 5432 | PostgreSQL | Only for direct DB access     |
| 9443 | Portainer  | Optional, HTTPS management UI |

## Troubleshooting

### "Cannot connect to the Docker daemon"
→ Make sure Docker Desktop is running (check system tray)

### Build fails on frontend
→ Check Node version in Dockerfile matches what the project needs (22)

### API unhealthy
→ Check logs: `docker compose -f docker-compose.staging.yml logs api`
→ Usually a DB connection issue — verify `.env.staging` values

### Can't reach from other machines
→ Check Windows Firewall — port 80 needs to be open for inbound
→ Run in PowerShell (admin):
```powershell
New-NetFirewallRule -DisplayName "WattsOn HTTP" -Direction Inbound -Port 80 -Protocol TCP -Action Allow
```
