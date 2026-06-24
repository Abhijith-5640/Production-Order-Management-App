# Deployment Guide

This guide explains how to publish the **Nexus Prod** API + React SPA into a single self-contained Windows executable and run it as a Windows Service so the app is always up and starts automatically with the host.

---

## Architecture

- **API**: ASP.NET Core 8 minimal API (`src/NexusProd.Api/`). The csproj is configured for `win-x64`, `SelfContained`, and `PublishSingleFile` — the build emits one `.exe` with the .NET runtime inside it. No .NET install is needed on the target machine.
- **SPA**: Vite builds the React client into `src/NexusProd.Api/wwwroot/`. The API serves the bundle at `/` and the JSON API at `/api/...`. The Vite build is wired into the .NET `BeforeBuild` target (see `NexusProd.Api.csproj`), so a plain `dotnet build` also produces a runnable single-folder app.
- **Network**: the API binds to `0.0.0.0:5099` by default. Other machines on the LAN reach the UI at `http://<host-ip>:5099/`.
- **External config**: `db_config.json` lives next to the published `.exe` and is written by the in-app Settings page on first run.

---

## Prerequisites

- **.NET 8 SDK** on the build machine (the runtime is bundled into the published output, so the target Windows box does **not** need .NET installed).
- **Node.js 18+** and **npm** on the build machine (for the Vite client build).
- A Windows machine for the service. x64 only.

---

## Step 1: Build the SPA

From the project root:

```bash
npm install --prefix client
npm run build --prefix client
```

This populates `src/NexusProd.Api/wwwroot/` with `index.html`, `assets/`, and `favicon.svg`. If you skip this and run `dotnet build` the csproj will run the same Vite build automatically — you only need to do it manually for iteration speed.

## Step 2: Publish the API

From the project root:

```bash
npm run publish:api
```

This is just a wrapper around:

```bash
dotnet publish src/NexusProd.Api -c Release -r win-x64
```

The output lands at:

```
src/NexusProd.Api/bin/Release/net8.0/win-x64/publish/NexusProd.Api.exe
src/NexusProd.Api/bin/Release/net8.0/win-x64/publish/NexusProd.Api.dll
src/NexusProd.Api/bin/Release/net8.0/win-x64/publish/appsettings.json
src/NexusProd.Api/bin/Release/net8.0/win-x64/publish/appsettings.Development.json
src/NexusProd.Api/bin/Release/net8.0/win-x64/publish/Resources/default_db_config.json
src/NexusProd.Api/bin/Release/net8.0/win-x64/publish/wwwroot/...
```

Single-file publishing with `IncludeNativeLibrariesForSelfExtract=true` means the `.exe` and the `.dll` are the same artifact — you can ship just the `.exe` if you want, but keeping the folder form makes `db_config.json` easier to find.

## Step 3: Install as a Windows Service

We use [WinSW](https://github.com/winsw/winsw) to register the published `.exe` with the Service Control Manager.

### 3.1 — Set up the service folder

Pick an install location. `%ProgramFiles%\NexusProd` is the convention; pick somewhere writable if `%ProgramFiles%` is locked down.

1. Create the folder: `mkdir "%ProgramFiles%\NexusProd"`.
2. Copy the entire `bin/Release/net8.0/win-x64/publish/` contents into it.
3. Download `WinSW-x64.exe` from the [WinSW releases page](https://github.com/winsw/winsw/releases) and place it in the same folder.
4. Create `NexusProd.Api.xml` next to `WinSW-x64.exe`. (WinSW looks for `<executable-name>.xml`; the published exe is `NexusProd.Api.exe`.) Use the template below.

The relevant parts of the XML:

```xml
<service>
  <id>NexusProd</id>
  <name>NexusProd API Server</name>
  <executable>%BASE%\NexusProd.Api.exe</executable>
  <workingdirectory>%BASE%</workingdirectory>
  <arguments>--urls=http://0.0.0.0:5099</arguments>
  <onfailure action="restart" delay="5 sec"/>
  <onfailure action="restart" delay="10 sec"/>
  <onfailure action="restart" delay="30 sec"/>
  <resetfailure>1 hour</resetfailure>
  <startmode>Automatic</startmode>
</service>
```

> **Note on the executable name**: the csproj pins `AssemblyName=NexusProd.Api`, so the published binary is `NexusProd.Api.exe`. WinSW will look for `NexusProd.Api.xml` next to it. Don't rename the exe without renaming the XML in lockstep.

### 3.2 — Register and start the service

Open PowerShell **as Administrator** and `cd` into the install folder:

```powershell
.\WinSW-x64.exe install
.\WinSW-x64.exe start
```

Verify:

```powershell
Get-Service NexusProd
```

You should see `Running`. Logs are written to `%BASE%\logs\` (roll-by-size, 10MB × 8 files).

### Service controls

| Action | WinSW | Windows native | PowerShell |
| :--- | :--- | :--- | :--- |
| **Start** | `.\WinSW-x64.exe start` | `net start NexusProd` | `Start-Service NexusProd` |
| **Stop** | `.\WinSW-x64.exe stop` | `net stop NexusProd` | `Stop-Service NexusProd` |
| **Restart** | `.\WinSW-x64.exe restart` | — | `Restart-Service NexusProd` |
| **Status** | `.\WinSW-x64.exe status` | `sc query NexusProd` | `Get-Service NexusProd` |
| **Uninstall** | `.\WinSW-x64.exe uninstall` | `sc delete NexusProd` | — |

---

## Step 4: First-run database setup

1. Browse to `http://<host>:5099/` on any machine on the LAN. The login page loads; the API is anonymous for `/api/sections`, `/api/trips`, `/api/server-info`, and `/api/health`.
2. Log in with the seeded credentials (see the project README) or go through the wizard.
3. The Settings page calls `POST /api/config/save` to write `db_config.json` next to the `.exe`. The change is hot — no restart needed.
4. If you change the connection details and the connection drops, the API falls back to the embedded `Resources/default_db_config.json` on next start. Restart the service after fixing credentials:

   ```powershell
   Restart-Service NexusProd
   ```

## Step 5: Verify the deployment

From any LAN machine:

- `http://<host>:5099/api/health` → `{ "status": "ok", ... }`
- `http://<host>:5099/api/server-info` → `version`, `lanAddresses`, `port`
- `http://<host>:5099/` → the SPA login page

If `/api/health` returns OK but the SPA doesn't load, the React bundle wasn't published — re-run the Vite build and re-publish.

---

## Updating to a new version

1. Pull the new code on the build machine.
2. `npm install --prefix client` (only if `client/package.json` changed).
3. `npm run publish:api`.
4. Copy the new `publish/` contents over the live install folder.
5. `Restart-Service NexusProd`.

The published exe has a built-in updater (background service in the same process) that polls an external manifest URL; see `GET /api/updater/status` for the current phase. The updater is opt-in — the manual flow above works without it.

---

## Troubleshooting

| Symptom | Likely cause |
| :--- | :--- |
| Service starts then immediately stops | Missing `appsettings.json` in the install folder, or a port collision on 5099. Check `%BASE%\logs\NexusProd.Api*.log`. |
| `Unable to connect` from the LAN | Windows Firewall is blocking 5099. Run `New-NetFirewallRule -DisplayName "NexusProd" -Direction Inbound -LocalPort 5099 -Protocol TCP -Action Allow` as Admin. |
| 401 on every API call after redeploy | The browser still has the old refresh cookie; clear site data or wait for the cookie to expire. |
| SPA loads but `Failed to fetch` on login | CORS — the SPA must be served from the same origin as the API. If you put the SPA behind a reverse proxy, proxy `/api/` to the API origin. |
| Login succeeds but every subsequent call 401s | `JwtSettings:Secret` changed between deploys. The old access tokens are now invalid; users must log in again. |
