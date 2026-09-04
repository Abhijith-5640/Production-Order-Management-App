# NexusProd Build & Deployment Guide for QA

This document provides clear, step-by-step instructions for building and deploying the NexusProd application to QA environment.

> **Note for Windows Users:** This guide is written for **Windows PowerShell**. If you are using WSL, Git Bash, or another shell, see the [Shell Variants](#shell-variants) section at the bottom.

---

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Build Steps](#build-steps)
3. [Package Artefacts](#package-artefacts)
4. [Installation](#installation)
5. [Uninstallation](#uninstallation)
6. [QA Environment Deployment](#qa-environment-deployment)
7. [Post-Deployment Verification](#post-deployment-verification)
8. [Rollback](#rollback)
9. [Auto-Updater](#auto-updater)
10. [Important Notes](#important-notes)
11. [Shell Variants](#shell-variants)

---

## Prerequisites

### Build Machine Requirements

- .NET 8 SDK (Verify: `dotnet --version` -> should show 8.0.x)
- Node.js 18+ (Verify: `node --version` -> >= 18.0.0)
- npm 9+ (Verify: `npm --version` -> >= 9.0.0)
- Git (Verify: `git --version` -> any recent version)

### QA Environment Requirements

- Windows (x64 architecture)
- Port 8443 open and accessible from QA network
- MySQL Server reachable with proper credentials
- **Administrator privileges are NOT required** for standard client installations

---

## Build Steps

### Step 1: Clean Build Environment

```powershell
# Navigate to project root
cd "c:\Dev Works\Web POS\Production-Order-Management-App"

# Clean any previous build outputs
Remove-Item -Path "src/NexusProd.Api/bin/Release" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "src/NexusProd.Api/obj" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "client/node_modules/.cache" -Recurse -Force -ErrorAction SilentlyContinue

# Install dependencies (only if changed)
npm install --prefix client
```

### Step 2: Verify Configuration

**Check JWT Settings:**

```powershell
# Ensure access token lifetime is 15 minutes (not 1 minute)
Select-String -Path "src/NexusProd.Api/appsettings.json" -Pattern "AccessTokenLifetimeMinutes"
# Expected output contains: "AccessTokenLifetimeMinutes": 15,
```

**Key Configuration Values:**

- Access Token Lifetime: **15 minutes**
- Refresh Token Lifetime: **7 days** (unchanged)
- Updater: **Disabled** by default (enable in appsettings.json for client deployments)

### Step 3: Build Client (SPA)

```powershell
# Build the React application
npm run build --prefix client

# Verify output directory exists
Get-ChildItem "src/NexusProd.Api/wwwroot/" -Recurse | Select-Object FullName
```

**Expected Output:**

- `src/NexusProd.Api/wwwroot/index.html`
- `src/NexusProd.Api/wwwroot/assets/`
- `src/NexusProd.Api/wwwroot/favicon.svg`

### Step 4: Publish API

```powershell
# Publish the .NET application
npm run publish:api

# Verify published files
Get-ChildItem "src/NexusProd.Api/bin/Release/net8.0/win-x64/publish/"
```

**Expected Files in Publish Directory:**

```
src/NexusProd.Api/bin/Release/net8.0/win-x64/publish/
├── NexusProd.exe                    # User-space bootstrapper launcher
├── NexusProd.Api.exe                # API server process
├── appsettings.json                 # Main config
├── appsettings.Development.json     # Dev config
├── appsettings.local.json           # Local secrets (preserved)
├── version.json                     # Current build version
├── wwwroot/                        # React SPA
├── Resources/                       # Embedded resources
└── *.config                        .NET config files
```

> **Note:** Run `npm run publish:all` to produce both `NexusProd.exe` and `NexusProd.Api.exe` in the same directory along with `version.json`.

---

## Package Artefacts

### Step 5: Prepare Deployment Package

```powershell
# Navigate to publish directory
Set-Location "src/NexusProd.Api/bin/Release/net8.0/win-x64/publish/"

# Create timestamped ZIP
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
Compress-Archive -Path * -DestinationPath "nexusprod-qa-${timestamp}.zip" -Force

# Generate SHA-256 checksum and save to file
$hash = Get-FileHash "nexusprod-qa-${timestamp}.zip" -Algorithm SHA256
$hash | Out-File -FilePath "nexusprod-qa-${timestamp}.zip.sha256" -Encoding ASCII

# List files in package
Get-ChildItem "nexusprod-qa-${timestamp}.zip"*
```

### Step 6: Document Build Information

Update the deployment checklist with build details:

```markdown
**Build Details:**

- Build Date: 2026-07-08
- Version: [check git tag or commit hash]
- Access Token Lifetime: 15 minutes
- Build Machine: [machine name]
- Build By: [your name]
```

---

## Installation

### Prerequisites

- Windows x64 machine
- No administrator privileges required
- PowerShell 5.1 or later

### Step A: Copy Deployment Package

Transfer the built ZIP file to the target machine:

```powershell
# From build machine - copy to target (example paths)
$publishDir = "src/NexusProd.Api/bin/Release/net8.0/win-x64/publish"
$zipFile = Get-ChildItem "$publishDir\nexusprod-qa-*.zip" | Sort-Object Name -Descending | Select-Object -First 1
Copy-Item -Path $zipFile.FullName -Destination "\\target-server\C$\Works\POS\" -Force
Copy-Item -Path "$($zipFile.FullName).sha256" -Destination "\\target-server\C$\Works\POS\" -Force
```

### Step B: Extract Files

```powershell
# On target machine - create installation directory
New-Item -ItemType Directory -Path "C:\Works\POS\NexusProd" -Force

# Extract the ZIP contents
Expand-Archive -Path "C:\Works\POS\nexusprod-qa-*.zip" -DestinationPath "C:\Works\POS\NexusProd" -Force

# Verify extracted files
Get-ChildItem "C:\Works\POS\NexusProd\" | Select-Object Name, Length
```

**Expected files in installation directory:**

```
C:\Works\POS\NexusProd\
├── NexusProd.exe                    # User-space bootstrapper launcher (64 MB)
├── NexusProd.Api.exe                # API server process (47 MB)
├── appsettings.json                 # Main configuration
├── appsettings.Development.json     # Development overrides
├── appsettings.local.json          # Local secrets (create if needed)
├── version.json                    # Current version
├── db_config.json                  # Database config (created on first run)
├── wwwroot/                        # React SPA
├── Resources/                      # Embedded resources
└── logs/                          # Application logs (created on first run)
```

### Step C: Configure Settings (Optional)

Edit `appsettings.json` to configure auto-updater:

```json
{
  "UpdateServerSettings": {
    "Url": "https://updates.tradersm.com",
    "CheckIntervalMinutes": 30,
    "Enabled": true
  }
}
```

### Step D: Register Autostart

```powershell
# Register NexusProd.exe to start automatically on user login (NO ADMIN REQUIRED)
Set-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" `
    -Name "NexusProd" `
    -Value "C:\Works\POS\NexusProd\NexusProd.exe"

# Verify registration
Get-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "NexusProd"
```

### Step E: Start the Application

```powershell
# Start NexusProd launcher
Start-Process "C:\Works\POS\NexusProd\NexusProd.exe"

# Wait for startup
Start-Sleep -Seconds 10

# Verify both processes are running
Get-Process -Name NexusProd, NexusProd.Api -ErrorAction SilentlyContinue
```

### Step F: Initial Setup via Web UI

1. Open browser and navigate to: `http://localhost:8443`
2. You should see the NexusProd login page
3. Login with admin credentials
4. Navigate to **Settings**
5. Configure database connection:
   - Host: [MySQL server IP]
   - Port: 3306
   - Database: prod_app
   - Username: [DB username]
   - Password: [DB password]
6. Click **Test Connection** -> Should show "Connection successful"
7. Click **Save Configuration**

**Configuration file location:** `C:\Works\POS\NexusProd\db_config.json`

---

## Uninstallation

### Complete Uninstall Process

### Step 1: Stop All Processes

```powershell
# Stop both launcher and API processes
Stop-Process -Name NexusProd -ErrorAction SilentlyContinue
Stop-Process -Name NexusProd.Api -ErrorAction SilentlyContinue

# Wait for processes to exit
Start-Sleep -Seconds 2

# Verify no processes remain
Get-Process -Name NexusProd*, NexusProd.Api* -ErrorAction SilentlyContinue
# Should return empty
```

### Step 2: Remove Autostart Registration

```powershell
# Remove registry autostart entry
Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" `
    -Name "NexusProd" -ErrorAction SilentlyContinue

# Verify removal
Get-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" `
    -Name "NexusProd" -ErrorAction SilentlyContinue
# Should return error (not found)
```

### Step 3: Delete Installation Folder

```powershell
# Delete the entire installation directory
Remove-Item -Path "C:\Works\POS\NexusProd" -Recurse -Force

# Verify deletion
Test-Path "C:\Works\POS\NexusProd"
# Should return False
```

### Step 4: Verify Complete Removal

```powershell
# Check for any remaining NexusProd processes
Get-Process | Where-Object { $_.ProcessName -like "*NexusProd*" }

# Check for any remaining files
Get-ChildItem -Path "C:\Works\POS\" -Recurse -Filter "*NexusProd*" -ErrorAction SilentlyContinue

# Check for any remaining registry entries
Get-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" `
    -ErrorAction SilentlyContinue | Select-Object NexusProd
```

### Quick Uninstall One-Liner

For complete uninstallation in one command:

```powershell
Stop-Process -Name NexusProd, NexusProd.Api -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "NexusProd" -ErrorAction SilentlyContinue
Remove-Item -Path "C:\Works\POS\NexusProd" -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "NexusProd has been uninstalled completely."
```

### What Gets Removed

| Item                | Location                                   | Removed |
| ------------------- | ------------------------------------------ | ------- |
| Launcher executable | `C:\Works\POS\NexusProd\NexusProd.exe`     | Yes     |
| API executable      | `C:\Works\POS\NexusProd\NexusProd.Api.exe` | Yes     |
| Application logs    | `C:\Works\POS\NexusProd\logs\`             | Yes     |
| Configuration files | `C:\Works\POS\NexusProd\appsettings.json`  | Yes     |
| Database config     | `C:\Works\POS\NexusProd\db_config.json`    | Yes     |
| React SPA           | `C:\Works\POS\NexusProd\wwwroot\`          | Yes     |
| Registry autostart  | `HKCU:\...\Run\NexusProd`                  | Yes     |
| Processes           | Running instances                          | Yes     |

### Before Uninstalling - Backup Data (Optional)

If you want to preserve data before uninstalling:

```powershell
# Backup database configuration
Copy-Item "C:\Works\POS\NexusProd\db_config.json" "C:\Works\POS\db_config.json.bak"

# Backup logs
Copy-Item "C:\Works\POS\NexusProd\logs" "C:\Works\POS\NexusProd-logs" -Recurse

# Backup database (if local MySQL)
# Use mysqldump or MySQL Workbench to export the database
```

---

## QA Environment Deployment

For deploying a new version to an existing installation:

### Step Q1: Stop and Backup

```powershell
# Stop existing processes
Stop-Process -Name NexusProd.Api -ErrorAction SilentlyContinue
Stop-Process -Name NexusProd -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

# Backup current installation
$dateSuffix = Get-Date -Format "yyyyMMdd-HHmmss"
if (Test-Path "C:\Works\POS\NexusProd") {
    Move-Item -Path "C:\Works\POS\NexusProd" -Destination "C:\Works\POS\NexusProd-Bak-$dateSuffix"
}
Get-ChildItem "C:\Works\POS\NexusProd-Bak-$dateSuffix"
```

### Step Q2: Deploy New Version

```powershell
# Extract new version (ZIP already copied to C:\Works\POS\)
$zipOnServer = Get-ChildItem "C:\Works\POS\nexusprod-qa-*.zip" | Sort-Object Name -Descending | Select-Object -First 1
Expand-Archive -Path $zipOnServer.FullName -DestinationPath "C:\Works\POS\NexusProd" -Force
Get-ChildItem "C:\Works\POS\NexusProd" | Select-Object Name, LastWriteTime
```

### Step Q3: Register Autostart

```powershell
Set-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" `
    -Name "NexusProd" -Value "C:\Works\POS\NexusProd\NexusProd.exe"
```

### Step Q4: Verify and Start

```powershell
# Start the application
Start-Process "C:\Works\POS\NexusProd\NexusProd.exe"
Start-Sleep -Seconds 10

# Verify both processes are running
Get-Process -Name NexusProd, NexusProd.Api -ErrorAction SilentlyContinue
```

---

## Post-Deployment Verification

### Step V1: Health Checks

```powershell
# Check API health
curl http://qa-server:8443/api/health
# Expected: { "status": "ok", "timestamp": "...", "version": "..." }

# Check server info
curl http://qa-server:8443/api/server-info
# Expected: { "version": "...", "lanAddresses": [...], "port": 8443 }
```

### Step V2: Log Verification

```powershell
# Check launcher log
Get-Content "C:\Works\POS\NexusProd\logs\launcher.log" -Tail 20

# Check API log
Get-Content "C:\Works\POS\NexusProd\logs\api.log" -Tail 20
```

### Step V3: Browser Verification

1. Open browser: `http://qa-server:8443`
2. Should see Login page
3. Login with credentials
4. Verify Dashboard loads
5. Test a simple API call

### Step V4: Token Verification

```powershell
# Login to verify token lifetime
curl -X POST http://qa-server:8443/api/auth/login `
  -H "Content-Type: application/json" `
  -d '{"username":"admin","password":"password"}'
# Verify access token shows 15 minutes expiry
```

---

## Rollback

### Step R1: Rollback if Needed

```powershell
# Stop processes
Stop-Process -Name NexusProd.Api -ErrorAction SilentlyContinue
Stop-Process -Name NexusProd -ErrorAction SilentlyContinue

# Remove failed installation
Remove-Item -Path "C:\Works\POS\NexusProd" -Recurse -Force

# Restore from backup (replace timestamp)
$backupSuffix = "20240708-120000"
Move-Item -Path "C:\Works\POS\NexusProd-Bak-$backupSuffix" -Destination "C:\Works\POS\NexusProd"

# Start
Start-Process "C:\Works\POS\NexusProd\NexusProd.exe"
```

---

## Auto-Updater

The user-space launcher (`NexusProd.exe`) runs the API (`NexusProd.Api.exe`) as a child process and automatically handles updates.

### Enable Auto-Updater

Edit `appsettings.json`:

```json
{
  "UpdateServerSettings": {
    "Url": "https://updates.tradersm.com",
    "CheckIntervalMinutes": 30,
    "Enabled": true
  }
}
```

### How Updates Work

1. **Launcher starts API** as a child process with redirected stdout/stderr
2. **Background updater** polls the update server at the configured interval
3. **When update available**, API downloads `update-pending.zip` to install directory
4. **API exits** with code 100 to signal launcher
5. **Launcher extracts** the zip and overwrites files (NexusProd.Api.exe -> .new to avoid locked files)
6. **Launcher restarts** the API with the new version

### Update Log Locations

| Log File            | Purpose                                |
| ------------------- | -------------------------------------- |
| `logs/launcher.log` | Launcher activity, restart events      |
| `logs/api.log`      | API stdout with `[API]` prefix         |
| `logs/api.log.old`  | Previous API log (auto-rolled at 10MB) |

### Monitor Update Progress

```powershell
# Watch launcher logs
Get-Content "C:\Works\POS\NexusProd\logs\launcher.log" -Wait

# Expected sequence:
# [timestamp] [Launcher] NexusProd Launcher starting.
# [timestamp] [Launcher] Starting NexusProd.Api.exe (crash #1/5)...
# [timestamp] [API] API server starting...
# [timestamp] [Launcher] NexusProd.Api.exe exited with code 100 after Xs.
# [timestamp] [Launcher] Found update-pending.zip - extracting update...
# [timestamp] [Launcher] Update extraction complete.
# [timestamp] [Launcher] Starting NexusProd.Api.exe...
```

### Crash Recovery

- If API crashes (non-zero exit code), launcher waits 3 seconds and restarts
- After 5 consecutive crashes within 60 seconds, launcher shuts down
- Delete `logs/` folder to reset crash counter

**No admin privileges required** - the launcher runs as a user-space background application.

---

## Important Notes

### 1. Access Token Lifetime Changes

- **Changed**: 1 minute -> **15 minutes**
- **Purpose**: Allow longer sessions without frequent refreshes
- **QA Focus**: Verify silent refresh works within 15 minutes

### 2. Launcher vs Windows Service

| Feature            | Old (WinSW)         | New (Launcher)                      |
| :----------------- | :------------------ | :---------------------------------- |
| Auto-start         | Windows Service     | Registry Run key                    |
| Process management | Service controls    | Child process monitor               |
| Update mechanism   | Task Scheduler task | Direct file replacement             |
| Admin privileges   | Required            | **Not required**                    |
| Logs               | Service logs        | `logs/launcher.log`, `logs/api.log` |

### 3. File Locations

| Item         | Location                                   |
| ------------ | ------------------------------------------ |
| Launcher     | `C:\Works\POS\NexusProd\NexusProd.exe`     |
| API          | `C:\Works\POS\NexusProd\NexusProd.Api.exe` |
| Config       | `C:\Works\POS\NexusProd\db_config.json`    |
| Logs         | `C:\Works\POS\NexusProd\logs\`             |
| Launcher Log | `C:\Works\POS\NexusProd\logs\launcher.log` |
| API Log      | `C:\Works\POS\NexusProd\logs\api.log`      |

### 4. Common Issues

| Symptom                  | Solution                                                                    |
| :----------------------- | :-------------------------------------------------------------------------- |
| Processes not running    | Start with: `Start-Process "C:\Works\POS\NexusProd\NexusProd.exe"`          |
| Port 8443 already in use | Find process: `Get-NetTCPConnection -LocalPort 8443`                        |
| API health fails         | Check MySQL connection, firewall, permissions                               |
| SPA not loading          | Verify wwwroot directory exists and has files                               |
| Launcher keeps crashing  | Check `logs/launcher.log` for errors; delete `logs/` to reset crash counter |
| API keeps restarting     | Check `logs/api.log` for errors                                             |

### 5. Process Controls

| Action            | Command                                                                                                                           |
| :---------------- | :-------------------------------------------------------------------------------------------------------------------------------- |
| **Start**         | `Start-Process "C:\Works\POS\NexusProd\NexusProd.exe"`                                                                            |
| **Stop launcher** | `Stop-Process -Name NexusProd -ErrorAction SilentlyContinue`                                                                      |
| **Stop API only** | `Stop-Process -Name NexusProd.Api -ErrorAction SilentlyContinue`                                                                  |
| **Restart**       | `Stop-Process -Name NexusProd, NexusProd.Api -ErrorAction SilentlyContinue; Start-Process "C:\Works\POS\NexusProd\NexusProd.exe"` |
| **Check status**  | `Get-Process -Name NexusProd, NexusProd.Api -ErrorAction SilentlyContinue`                                                        |

### 6. QA Testing Focus Areas

1. **Access token expires after 15 minutes** (not 1 minute)
2. **Silent refresh mechanism** works seamlessly
3. **Login/logout functionality** as expected
4. **Core order management features**
5. **Database connectivity** and configuration
6. **Frontend UI responsiveness**

---

## Final Checklist

- [ ] Build completed without errors
- [ ] Access token lifetime verified (15 minutes)
- [ ] Package created with checksum
- [ ] QA environment backed up
- [ ] Processes stopped before deployment
- [ ] Application installed successfully
- [ ] Autostart registered (registry)
- [ ] Database configured and tested
- [ ] Health endpoints returning 200 OK
- [ ] SPA loading correctly
- [ ] Logs verified (`logs/launcher.log`, `logs/api.log`)
- [ ] QA team notified of deployment
- [ ] QA release checklist documented

---

## Shell Variants

### Git Bash / WSL / macOS / Linux

If you are **not** using Windows PowerShell, use the original Unix/Linux commands shown below for **Build Steps 1-5 only**. Installation onwards requires PowerShell (target server is Windows).

```bash
# Step 1: Clean Build Environment (Unix shells)
cd "c:/Dev Works/Web POS/Production-Order-Management-App"
rm -rf src/NexusProd.Api/bin/Release/
rm -rf src/NexusProd.Api/obj/
rm -rf client/node_modules/.cache
npm install --prefix client

# Step 2: Verify Configuration
grep -n "AccessTokenLifetimeMinutes" src/NexusProd.Api/appsettings.json

# Step 3: Build Client
npm run build --prefix client
ls -la src/NexusProd.Api/wwwroot/

# Step 4: Publish API
npm run publish:api
ls -la src/NexusProd.Api/bin/Release/net8.0/win-x64/publish/

# Step 5: Package Artefacts
cd src/NexusProd.Api/bin/Release/net8.0/win-x64/publish/
TIMESTAMP=$(date +"%Y%m%d-%H%M%S")
zip -r "nexusprod-qa-${TIMESTAMP}.zip" .
sha256sum "nexusprod-qa-${TIMESTAMP}.zip" > "nexusprod-qa-${TIMESTAMP}.zip.sha256"
ls -lah "nexusprod-qa-${TIMESTAMP}.zip"*
```

> **Important:** Do **not** run `rm -rf` on paths containing spaces without quoting them. In Git Bash/WSL, `c:/Dev\ Works/Web\ POS/...` works, but plain `c:\Dev Works\...` does not.
