# 🚀 NexusProd Deployment Guide

This guide explains how to publish the **Nexus Prod** API + React SPA into a single self-contained Windows executable and run it as a user-space background application that starts automatically with the user's session.

---

## Architecture

- **Launcher**: `NexusProd.exe` - User-space bootstrapper that starts the API and monitors its lifetime
- **API**: `NexusProd.Api.exe` - ASP.NET Core 8 minimal API with the .NET runtime bundled inside
- **SPA**: Built into `wwwroot/` by the API at runtime
- **Network**: The API binds to `0.0.0.0:5099` by default. Other machines on the LAN reach the UI at `http://<host-ip>:5099/`
- **External config**: `db_config.json` lives next to the published executables and is written by the in-app Settings page

---

## Prerequisites

- **.NET 8 SDK** on the build machine (the runtime is bundled into the output)
- **Node.js 18+** and **npm** on the build machine (for the React client build)
- A Windows machine for deployment (x64 only)
- **No admin privileges required** for user-space deployment

---

## Step 1: Build the SPA

From the project root:

```bash
npm install --prefix client
npm run build --prefix client
```

This populates `src/NexusProd.Api/wwwroot/` with the React application. The API serves it at `/`.

## Step 2: Publish the Application

From the project root:

```bash
npm run publish:all
```

This builds and publishes both the launcher (`NexusProd.exe`) and the API (`NexusProd.Api.exe`).

The output lands at:

```
src/NexusProd.Api/bin/Release/net8.0/win-x64/publish/
├── 📄 NexusProd.exe                  # User-space bootstrapper launcher
├── 📄 NexusProd.Api.exe              # ASP.NET Core API server
├── 📄 appsettings.json               # Main config
├── 📄 appsettings.Development.json    # Dev config
├── 📄 version.json                   # Current version
├── 📁 wwwroot/                       # React SPA
├── 📁 Resources/                    # Embedded resources
└── 📄 *.config                       .NET config files
```

## Step 3: Install as a User-Space Background Application

We use the built-in Windows Registry to auto-start the application on user login.

### 3.1 — Set up the application folder

1. Create your install folder: `mkdir "C:\Works\POS\NexusProd"`
2. Copy the entire `publish/` contents into it
3. Verify both executables exist:
   - `C:\Works\POS\NexusProd\NexusProd.exe`
   - `C:\Works\POS\NexusProd\NexusProd.Api.exe`

### 3.2 — Register autostart

Open PowerShell **as the target user** (no admin needed):

```powershell
Set-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" `
    -Name "NexusProd" `
    -Value "C:\Works\POS\NexusProd\NexusProd.exe"
```

> **Note**: This only requires write access to the registry for the current user, not admin privileges.

### 3.3 — Start the application

```powershell
Start-Process "C:\Works\POS\NexusProd\NexusProd.exe"
```

The launcher will start the API as a background process without showing any windows.

### 3.4 — Verify installation

```powershell
# Check both processes are running
Get-Process -Name NexusProd, NexusProd.Api

# Should show both processes:
# NexusProd — launcher process
# NexusProd.Api — API server process

# Check logs for startup messages
Get-Content "C:\Works\POS\NexusProd\logs\launcher.log" -Tail 5
```

### Process controls

| Action | Command |
| :--- | :--- |
| **Start** | `Start-Process "C:\Works\POS\NexusProd\NexusProd.exe"` |
| **Stop launcher** | `Stop-Process -Name NexusProd -ErrorAction SilentlyContinue` |
| **Stop API only** | `Stop-Process -Name NexusProd.Api -ErrorAction SilentlyContinue` |
| **Restart** | Stop both, then start the launcher |
| **Check status** | `Get-Process -Name NexusProd, NexusProd.Api -ErrorAction SilentlyContinue` |

### Registry autostart controls

| Action | Command |
| :--- | :--- |
| **Enable autostart** | `Set-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "NexusProd" -Value "C:\Works\POS\NexusProd\NexusProd.exe"` |
| **Disable autostart** | `Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "NexusProd" -ErrorAction SilentlyContinue` |

---

## Step 4: First-run database setup

1. Browse to `http://<host>:5099/` on any machine on the LAN
2. Log in with the seeded credentials or go through the wizard
3. The Settings page calls `POST /api/config/save` to write `db_config.json`
4. The change is hot — no restart needed

**Configuration file location:**
```
C:\Works\POS\NexusProd\db_config.json
```

---

## Step 5: Verify the deployment

From any LAN machine:

- `http://<host>:5099/api/health` → `{ "status": "ok", ... }`
- `http://<host>:5099/api/server-info` → `version`, `lanAddresses`, `port`
- `http://<host>:5099/` → the SPA login page

If `/api/health` returns OK but the SPA doesn't load, the React bundle wasn't published — re-run the Vite build.

---

## Updating to a new version

1. Pull the new code on the build machine
2. `npm install --prefix client` (only if client dependencies changed)
3. `npm run publish:all`
4. Copy the new `publish/` contents over the live install folder
5. Stop both processes:
   ```powershell
   Stop-Process -Name NexusProd.Api -ErrorAction SilentlyContinue
   Stop-Process -Name NexusProd -ErrorAction SilentlyContinue
   ```
6. Start the launcher:
   ```powershell
   Start-Process "C:\Works\POS\NexusProd\NexusProd.exe"
   ```

The launcher will automatically detect the updated files and start the API.

---

## Auto-Update Mechanism

The launcher includes a built-in update mechanism:

### Update Flow

1. The API checks for updates in the background (if enabled)
2. When an update is available, it downloads `update-pending.zip`
3. The API calls `Environment.Exit(100)` to signal the launcher
4. The launcher detects the exit code and:
   - Extracts the update package to a temporary folder
   - Overwrites existing files (skipping locked files)
   - If the new API is `NexusProd.Api.exe`, it's written as `NexusProd.Api.exe.new`
   - Starts the new API process
5. The new API reads `.new` files on startup and replaces them

### Update Log Locations

- `logs/launcher.log` — Launcher activity and update status
- `logs/api.log` — API stdout/stderr with `[API]` prefix
- `logs/api.log.old` — Rolled API log when it exceeds 10MB

### Update Status

Check the current update status:

```bash
# Check launcher logs for update activity
Get-Content "C:\Works\POS\NexusProd\logs\launcher.log" -Tail 20

# Check API logs for update requests
Get-Content "C:\Works\POS\NexusProd\logs\api.log" -Tail 20
```

---

## Troubleshooting

| Symptom | Solution |
| :--- | :--- |
| No processes running | Start with: `Start-Process "C:\Works\POS\NexusProd\NexusProd.exe"` |
| Application doesn't start | Check `logs/launcher.log` for errors |
| API doesn't start | Check `logs/api.log` for errors |
| Port 5099 already in use | Find process: `Get-NetTCPConnection -LocalPort 5099` |
| `Unable to connect` from LAN | Windows Firewall may be blocking port 5099 |
| 401 on every API call | Browser may have old refresh cookie - clear site data |
| Login succeeds but subsequent calls 401 | `JwtSettings:Secret` changed between deployments |
| Application keeps restarting | Check crash counter in `logs/`; delete `logs/` to reset |

### Common Log Locations

```
C:\Works\POS\NexusProd\logs\launcher.log    — Launcher activity
C:\Works\POS\NexusProd\logs\api.log         — API output
C:\Works\POS\NexusProd\logs\api.log.old     — Previous API log
C:\Works\POS\NexusProd\logs\crash-count.txt — Crash tracking
C:\Works\POS\NexusProd\logs\first-crash.txt — First crash timestamp
```

### Debug Mode

To enable debug logging:

1. Add to `appsettings.local.json`:
   ```json
   {
     "Logging": {
       "LogLevel": {
         "Default": "Debug",
         "Microsoft.AspNetCore": "Warning"
       }
     }
   }
   ```

2. Restart the application:
   ```powershell
   Stop-Process -Name NexusProd.Api -ErrorAction SilentlyContinue
   Stop-Process -Name NexusProd -ErrorAction SilentlyContinue
   Start-Process "C:\Works\POS\NexusProd\NexusProd.exe"
   ```

---

## Important Notes

1. **No Admin Required**: The user-space launcher runs without Windows Service admin privileges
2. **Auto-Restart**: The launcher automatically restarts the API if it crashes
3. **Log Rotation**: API logs automatically roll to `.old` when they exceed 10MB
4. **Update Detection**: The launcher detects and applies updates without user interaction
5. **Clean Shutdown**: Use `Stop-Process -Name NexusProd` for a clean shutdown
6. **Port Conflict**: Ensure no other application uses port 5099

---
