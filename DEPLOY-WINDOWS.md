# WattsOn — Windows Server Deployment (No Docker)

Native deployment on Windows Server 2022. No Docker, no WSL2, no containers.

## Architecture

```
Browser (LAN)
    │
    ▼
IIS (:80)
    ├── Static files (React app)
    └── /api/* → reverse proxy → Kestrel (:5100)
                                    │
                                    ▼
                              PostgreSQL (:5432)
                                    ▲
                                    │
                        Worker (Windows Service)
```

## What You Need

Pre-built artifacts are in `publish/staging/`:
- `api/` — .NET API (Kestrel, port 5100)
- `worker/` — Background worker (DataHub polling, settlements)
- `frontend/` — Built React app (static files)

---

## Step 1: Install PostgreSQL + TimescaleDB

### PostgreSQL 16

1. Download: https://www.enterprisedb.com/downloads/postgres-postgresql-downloads (Windows x86-64, v16)
2. Run installer, use defaults:
   - Port: **5432**
   - Superuser password: pick something, you'll need it
   - Locale: default
3. **Uncheck** "Launch Stack Builder" at the end

### TimescaleDB Extension

1. Download: https://docs.timescale.com/self-hosted/latest/install/installation-windows/
2. Run the installer — it auto-detects the PostgreSQL install
3. Restart PostgreSQL service (Services → `postgresql-x64-16` → Restart)

### Create the Database

Open **pgAdmin** (installed with PostgreSQL) or use psql:

```sql
CREATE USER wattson WITH PASSWORD 'pick_a_strong_password';
CREATE DATABASE wattson OWNER wattson;
\c wattson
CREATE EXTENSION IF NOT EXISTS timescaledb;
```

---

## Step 2: Install .NET 9 Runtime

The API and Worker need the .NET 9 runtime (not the SDK).

1. Download **ASP.NET Core Runtime 9.0** (Windows x64 — Hosting Bundle):
   https://dotnet.microsoft.com/en-us/download/dotnet/9.0
   → "ASP.NET Core Runtime" → "Hosting Bundle" (includes both runtime + IIS module)
2. Run the installer
3. Verify: open PowerShell → `dotnet --list-runtimes` should show 9.0.x entries

---

## Step 3: Deploy Files

Copy the `publish/staging/` folder to the server. Suggested layout:

```
C:\WattsOn\
    api\            ← contents of publish/staging/api
    worker\         ← contents of publish/staging/worker
    frontend\       ← contents of publish/staging/frontend
```

### Configure Connection Strings

Edit `C:\WattsOn\api\appsettings.Staging.json`:
```json
{
  "ConnectionStrings": {
    "WattsOn": "Host=localhost;Port=5432;Database=wattson;Username=wattson;Password=YOUR_DB_PASSWORD"
  }
}
```

Edit `C:\WattsOn\worker\appsettings.Staging.json` — same connection string.

---

## Step 4: Set Up IIS

### Enable IIS (if not already)

PowerShell (admin):
```powershell
Install-WindowsFeature -Name Web-Server -IncludeManagementTools
```

### Install URL Rewrite Module

Required for reverse proxy. Download:
https://www.iis.net/downloads/microsoft/url-rewrite

### Install ARR (Application Request Routing)

Required for reverse proxy. Download:
https://www.iis.net/downloads/microsoft/application-request-routing

After installing ARR, enable proxy in IIS:
1. Open **IIS Manager**
2. Click server name (top level)
3. Double-click **Application Request Routing Cache**
4. Click **Server Proxy Settings** (right panel)
5. Check **Enable proxy** → Apply

### Create the Site

1. Open **IIS Manager**
2. Right-click **Sites** → **Add Website**
   - Site name: `WattsOn`
   - Physical path: `C:\WattsOn\frontend`
   - Port: `80`
   - Host name: leave blank (or set `wattson.local` if you have DNS)
3. Remove or stop "Default Web Site" if it's using port 80

### Add Reverse Proxy Rule

Create `C:\WattsOn\frontend\web.config`:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<configuration>
  <system.webServer>
    <rewrite>
      <rules>
        <!-- Reverse proxy /api/* to Kestrel -->
        <rule name="API Proxy" stopProcessing="true">
          <match url="^api/(.*)" />
          <action type="Rewrite" url="http://localhost:5100/api/{R:1}" />
        </rule>
        <!-- SPA fallback: serve index.html for all non-file routes -->
        <rule name="SPA Fallback" stopProcessing="true">
          <match url=".*" />
          <conditions logicalGrouping="MatchAll">
            <add input="{REQUEST_FILENAME}" matchType="IsFile" negate="true" />
            <add input="{REQUEST_FILENAME}" matchType="IsDirectory" negate="true" />
          </conditions>
          <action type="Rewrite" url="/index.html" />
        </rule>
      </rules>
    </rewrite>
    <!-- Serve static files with correct MIME types -->
    <staticContent>
      <remove fileExtension=".json" />
      <mimeMap fileExtension=".json" mimeType="application/json" />
    </staticContent>
  </system.webServer>
</configuration>
```

---

## Step 5: Install API + Worker as Windows Services

### Download NSSM

NSSM (Non-Sucking Service Manager) lets you run any .exe as a Windows Service.

1. Download: https://nssm.cc/download
2. Extract to `C:\Tools\nssm\`
3. Add to PATH (optional): `C:\Tools\nssm\win64\`

### Install the API Service

PowerShell (admin):
```powershell
C:\Tools\nssm\win64\nssm.exe install WattsOnApi "C:\WattsOn\api\WattsOn.Api.exe"
C:\Tools\nssm\win64\nssm.exe set WattsOnApi AppDirectory "C:\WattsOn\api"
C:\Tools\nssm\win64\nssm.exe set WattsOnApi AppEnvironmentExtra "ASPNETCORE_ENVIRONMENT=Staging" "ASPNETCORE_URLS=http://localhost:5100"
C:\Tools\nssm\win64\nssm.exe set WattsOnApi DisplayName "WattsOn API"
C:\Tools\nssm\win64\nssm.exe set WattsOnApi Description "WattsOn Settlement Engine — API"
C:\Tools\nssm\win64\nssm.exe set WattsOnApi Start SERVICE_AUTO_START
C:\Tools\nssm\win64\nssm.exe set WattsOnApi AppStdout "C:\WattsOn\logs\api-stdout.log"
C:\Tools\nssm\win64\nssm.exe set WattsOnApi AppStderr "C:\WattsOn\logs\api-stderr.log"
C:\Tools\nssm\win64\nssm.exe set WattsOnApi AppRotateFiles 1
C:\Tools\nssm\win64\nssm.exe set WattsOnApi AppRotateBytes 10485760

# Create logs directory
New-Item -ItemType Directory -Path "C:\WattsOn\logs" -Force

# Start the service
Start-Service WattsOnApi
```

### Install the Worker Service

```powershell
C:\Tools\nssm\win64\nssm.exe install WattsOnWorker "C:\WattsOn\worker\WattsOn.Worker.exe"
C:\Tools\nssm\win64\nssm.exe set WattsOnWorker AppDirectory "C:\WattsOn\worker"
C:\Tools\nssm\win64\nssm.exe set WattsOnWorker AppEnvironmentExtra "DOTNET_ENVIRONMENT=Staging"
C:\Tools\nssm\win64\nssm.exe set WattsOnWorker DisplayName "WattsOn Worker"
C:\Tools\nssm\win64\nssm.exe set WattsOnWorker Description "WattsOn Settlement Engine — Background Worker"
C:\Tools\nssm\win64\nssm.exe set WattsOnWorker Start SERVICE_AUTO_START
C:\Tools\nssm\win64\nssm.exe set WattsOnWorker AppStdout "C:\WattsOn\logs\worker-stdout.log"
C:\Tools\nssm\win64\nssm.exe set WattsOnWorker AppStderr "C:\WattsOn\logs\worker-stderr.log"
C:\Tools\nssm\win64\nssm.exe set WattsOnWorker AppRotateFiles 1
C:\Tools\nssm\win64\nssm.exe set WattsOnWorker AppRotateBytes 10485760

Start-Service WattsOnWorker
```

---

## Step 6: Open Firewall

```powershell
New-NetFirewallRule -DisplayName "WattsOn HTTP" -Direction Inbound -Port 80 -Protocol TCP -Action Allow
```

---

## Step 7: Verify

1. On the server: `http://localhost` → WattsOn UI
2. From another machine: `http://<server-ip>` → WattsOn UI
3. Check services: `Get-Service WattsOnApi, WattsOnWorker`
4. Check API health: `http://localhost:5100/api/health`

---

## Updating

When you have new code:

1. Rebuild on dev machine (WSL2):
   ```bash
   cd /home/senmakj/source/wattson
   dotnet publish src/WattsOn.Api -c Release -r win-x64 --self-contained false -o publish/staging/api
   dotnet publish src/WattsOn.Worker -c Release -r win-x64 --self-contained false -o publish/staging/worker
   cd src/WattsOn.Frontend && npm run build -- --outDir ../../publish/staging/frontend
   ```

2. On the server (PowerShell admin):
   ```powershell
   Stop-Service WattsOnApi, WattsOnWorker

   # Copy new files (from network share, USB, git pull, etc.)
   Copy-Item \\DEV-MACHINE\...\publish\staging\api\* C:\WattsOn\api\ -Recurse -Force
   Copy-Item \\DEV-MACHINE\...\publish\staging\worker\* C:\WattsOn\worker\ -Recurse -Force
   Copy-Item \\DEV-MACHINE\...\publish\staging\frontend\* C:\WattsOn\frontend\ -Recurse -Force

   Start-Service WattsOnApi, WattsOnWorker
   ```

   IIS picks up static file changes automatically. No IIS restart needed.

---

## Troubleshooting

### API won't start
- Check `C:\WattsOn\logs\api-stderr.log`
- Most likely: wrong connection string or PostgreSQL not running
- Verify: `Test-NetConnection -ComputerName localhost -Port 5432`

### "502 Bad Gateway" in browser
- API service isn't running → `Start-Service WattsOnApi`
- Or API is on wrong port → check ASPNETCORE_URLS in NSSM config

### IIS shows default page instead of WattsOn
- "Default Web Site" is still active → stop it in IIS Manager
- Or port conflict → check bindings

### TimescaleDB extension not found
- Restart PostgreSQL after installing the extension
- Connect with psql: `SELECT default_version FROM pg_available_extensions WHERE name = 'timescaledb';`

### Check service status
```powershell
Get-Service WattsOnApi, WattsOnWorker | Format-Table Name, Status, StartType
nssm status WattsOnApi
nssm status WattsOnWorker
```
