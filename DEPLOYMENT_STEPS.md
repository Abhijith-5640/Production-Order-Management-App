# 🚀 NexusProd Build & Deployment Guide for QA

This document provides clear, step-by-step instructions for building and deploying the NexusProd application to QA environment.

> **Note for Windows Users:** This guide is written for **Windows PowerShell**. If you are using WSL, Git Bash, or another shell, see the [Shell Variants](#shell-variants) section at the bottom.

---

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Build Steps](#build-steps)
3. [Package Artefacts](#package-artefacts)
4. [QA Environment Deployment](#qa-environment-deployment)
5. [Post-Deployment Verification](#post-deployment-verification)
6. [Rollback Plan](#rollback-plan)
7. [Important Notes](#important-notes)
8. [Shell Variants](#shell-variants)

---

## Prerequisites

### Build Machine Requirements

- ✅ **.NET 8 SDK** (Verify: `dotnet --version` → should show 8.0.x)
- ✅ **Node.js 18+** (Verify: `node --version` → >= 18.0.0)
- ✅ **npm 9+** (Verify: `npm --version` → >= 9.0.0)
- ✅ **Git** (Verify: `git --version` → any recent version)

### QA Environment Requirements

- ✅ **Windows Server** (x64 architecture)
- ✅ **Port 8443** open and accessible from QA network
- ✅ **MySQL Server** reachable with proper credentials
- ✅ **Administrative access** for service installation

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

- ✅ Access Token Lifetime: **15 minutes**
- ❌ Refresh Token Lifetime: **7 days** (unchanged)
- ❌ Updater: **Disabled** by default

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
📁 src/NexusProd.Api/bin/Release/net8.0/win-x64/publish/
├── 📄 NexusProd.Api.exe              # Self-contained executable
├── 📄 NexusProd.Updater.Helper.exe    # Detached updater helper process
├── 📄 appsettings.json               # Main config
├── 📄 appsettings.Development.json    # Dev config
├── 📄 appsettings.local.json         # Local secrets (preserved)
├── 📄 version.json                   # Current build version
├── 📁 wwwroot/                       # React SPA
├── 📁 Resources/                    # Embedded resources
└── 📄 *.config                       .NET config files
```

> **Note:** After `npm run publish:api`, also run `npm run publish:all` (or `npm run publish:helper && npm run publish:updater-helper`) to produce `NexusProd.Updater.Helper.exe` and `version.json` in the same directory.

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
$hash = (Get-FileHash "nexusprod-qa-${timestamp}.zip" -Algorithm SHA256).Hash
$hash | Out-File -FilePath "nexusprod-qa-${timestamp}.zip.sha256" -Encoding ASCII

# List files in package
Get-ChildItem "nexusprod-qa-${timestamp}.zip"*
```

### Step 6: Document Build Information

Update the deployment checklist with build details:

```markdown
**Build Details:**

- Build Date: 2026-07-02
- Version: [check git tag or commit hash]
- Access Token Lifetime: 15 minutes
- Build Machine: [machine name]
- Build By: [your name]
```

---

## QA Environment Deployment

### Step 7: Backup Current Installation (if upgrading)

```powershell
# Connect to QA server via PowerShell remoting or console

# Stop existing service
Stop-Service -Name NexusProd

# Backup current installation
$dateSuffix = Get-Date -Format "yyyyMMdd-HHmmss"
Move-Item -Path "C:\Program Files\NexusProd" -Destination "C:\Program Files\NexusProd-Bak-$dateSuffix"

# Verify backup exists
Get-ChildItem "C:\Program Files\NexusProd-Bak-$dateSuffix"
```

### Step 8: Deploy New Version

```powershell
# Copy the ZIP file to QA server (run from build machine)
$publishDir = "src/NexusProd.Api/bin/Release/net8.0/win-x64/publish"
$zipFile = Get-ChildItem "$publishDir\nexusprod-qa-*.zip" | Sort-Object Name -Descending | Select-Object -First 1
Copy-Item -Path $zipFile.FullName -Destination "C:\Works\POS\" -Force
Copy-Item -Path "$($zipFile.FullName).sha256" -Destination "C:\Works\POS\" -Force

# On QA server (via PowerShell):
Set-Location "C:\Works\POS"

# Extract new version — use the most recent ZIP from temp
$zipOnServer = Get-ChildItem "C:\Works\POS\nexusprod-qa-*.zip" | Sort-Object Name -Descending | Select-Object -First 1
Expand-Archive -Path $zipOnServer.FullName -DestinationPath "C:\Works\POS\NexusProd" -Force

# Verify key files exist
Get-ChildItem "C:\Works\POS\NexusProd" | Select-Object Name, LastWriteTime
```

### Step 9: Install/Update Service

If this is a **new installation**:

```powershell
# Download WinSW if not present
Invoke-WebRequest -Uri "https://github.com/winsw/winsw/releases/latest/download/WinSW-x64.exe" -OutFile "C:\Works\POS\NexusProd\NexusProd.exe"

# Create service configuration
@"
<?xml version="1.0" encoding="utf-8" ?>
<service>
  <id>NexusProd</id>
  <name>NexusProd API Server</name>
  <description>NexusProd Production Order Management API</description>
  <executable>%BASE%\NexusProd.Api.exe</executable>
  <workingdirectory>%BASE%</workingdirectory>
  <arguments>--urls=http://0.0.0.0:8443</arguments>
  <onfailure action="restart" delay="5 sec"/>
  <onfailure action="restart" delay="10 sec"/>
  <onfailure action="restart" delay="30 sec"/>
  <resetfailure>1 hour</resetfailure>
  <startmode>Automatic</startmode>
  <logmode>roll</logmode>
  <logpath>%BASE%\logs</logpath>
</service>
"@ | Out-File -FilePath "C:\Works\POS\NexusProd\NexusProd.xml" -Encoding UTF8
```

If **upgrading**:

```powershell
# Update existing service (WinSW will auto-detect new executable)
.\WinSW-x64.exe update
```

### Step 10: Configure Database Connection

1. Open browser and navigate to: `http://qa-server:8443`
2. Login with valid credentials
3. Navigate to Settings page
4. Configure MySQL connection:
   - Host: [MySQL server IP]
   - Port: 3306 (or custom)
   - Database: prod_app
   - Username: [DB username]
   - Password: [DB password]
5. Click "Test Connection" → Should show "✅ Connection successful"
6. Click "Save Configuration"

**Note:** This creates `db_config.json` next to the executable. No restart needed for config changes.

---

## Client Machine Deployment (with Auto-Updater)

For client (production) installations, where automatic updates are enabled:

### Prerequisites

- The `UpdateServerSettings:Enabled = true` in `appsettings.json` (or `appsettings.local.json`).
- WinSW service ID **must exactly match** the `NEXUSPROD_WINSW_NAME` environment variable used when publishing, or default to `"NexusProd"`.
  - This hard requirement ensures the helper can stop the correct service.
- Verify that `NexusProd.Updater.Helper.exe` and `version.json` are present in the extracted install directory before first start.

### First-Time Installation

1. Follow **Steps 1–10** from the QA section above (build, publish, copy, extract).
2. **Verify updater components:**
   ```powershell
   # In the extracted install directory:
   Get-ChildItem "*.exe"
   # Should include: NexusProd.Api.exe AND NexusProd.Updater.Helper.exe
   Get-ChildItem "version.json"
   # Should exist: { "version": "..." }
   ```
3. **Configure `appsettings.json`:**
   ```json
   {
     "UpdateServerSettings": {
       "Url": "https://updates.tradersm.com",
       "CheckIntervalMinutes": 30,
       "Enabled": true
     }
   }
   ```
4. Install the service and start as described in Step 9 (QA section).

### Update Mechanism

- When an update is available, the detached `NexusProd.Updater.Helper.exe` handles the stop/swap/start sequence.
- The updater's log is available at `C:\Program Files\NexusProd\logs\updater-helper.log`.
- The helper waits for the main API process to exit cleanly before attempting `net stop`, avoiding the deadlock.
- If the helper fails, it attempts to restart the service to prevent a permanent outage.

---

## Post-Deployment Verification

### Step 11: Start Service and Verify

```powershell
# Start the service
Start-Service -Name NexusProd

# Wait for startup
Start-Sleep -Seconds 30

# Check service status
Get-Service -Name NexusProd
# Should show: Status = Running
```

### Step 12: Health Checks

**API Health Endpoints:**

```powershell
# Health check
curl http://qa-server:8443/api/health

# Expected response:
# {
#   "status": "ok",
#   "timestamp": "...",
#   "version": "..."
# }

# Server info
curl http://qa-server:8443/api/server-info

# Expected response:
# {
#   "version": "...",
#   "lanAddresses": ["..."],
#   "port": 8443
# }
```

**SPA Verification:**

1. Open browser: `http://qa-server:8443`
2. Should see Login page
3. Verify all elements load correctly
4. Login with test credentials
5. Verify Dashboard loads
6. Test a simple API call (e.g., get sections)

### Step 13: Token Verification (Critical)

**Test Access Token Lifecycle (15 minutes):**

```powershell
# Login via API to get tokens
curl -X POST http://qa-server:8443/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"password"}'

# Note the access token expiry time
# Should show 15 minutes from now
```

**Expected Token Lifetime Behavior:**

- ✅ Access token expires after exactly 15 minutes
- ✅ Silent refresh should work seamlessly before expiry
- ✅ Hard logout should occur for non-expiry 401 responses

---

## Rollback Plan

### Step 14: Rollback if Needed

```powershell
# If deployment fails, stop service and restore backup
# Replace the backupSuffix below with the actual timestamp from Step 7
$backupSuffix = "20240101-120000"
Stop-Service -Name NexusProd

# Remove failed installation
Remove-Item -Path "C:\Program Files\NexusProd" -Recurse -Force

# Restore from backup
Move-Item -Path "C:\Program Files\NexusProd-Bak-$backupSuffix" -Destination "C:\Program Files\NexusProd"

# Start old version
Start-Service -Name NexusProd
```

### Quick Rollback Commands

```powershell
# One-liner rollback (when you have timestamp)
$timestamp = "20240101-120000"
Stop-Service NexusProd
Remove-Item -Path "C:\Program Files\NexusProd" -Recurse -Force
Move-Item -Path "C:\Program Files\NexusProd-Bak-$timestamp" -Destination "C:\Program Files\NexusProd"
Start-Service NexusProd
```

---

## Important Notes

### 1. Access Token Lifetime Changes

- **Changed**: 1 minute → **15 minutes**
- **Purpose**: Allow longer sessions without frequent refreshes
- **QA Focus**: Verify silent refresh works within 15 minutes

### 2. Updater Functionality

- **QA**: Sets `UpdateServerSettings:Enabled = false` (explicitly disable auto-updates)
- **Client**: Sets `UpdateServerSettings:Enabled = true` to enable automatic updates
- **QA**: Do not test `/api/updater/*` endpoints (server-side only)
- **Client**: Auto-update runs in detached helper process; log at `logs/updater-helper.log`

### 3. File Locations

- **Executable**: `C:\Program Files\NexusProd\NexusProd.Api.exe`
- **Config**: `C:\Program Files\NexusProd\db_config.json`
- **Logs**: `C:\Program Files\NexusProd\logs\`
- **WinSW**: `C:\Program Files\NexusProd\WinSW-x64.exe`

### 4. Common Issues

| Symptom                   | Solution                                             |
| ------------------------- | ---------------------------------------------------- |
| Service starts then stops | Check logs at `C:\Program Files\NexusProd\logs\`     |
| Port 8443 already in use  | Find process: `Get-NetTCPConnection -LocalPort 8443` |
| API health fails          | Check MySQL connection, firewall, permissions        |
| SPA not loading           | Verify wwwroot directory exists and has files        |

### 5. QA Testing Focus Areas

1. **Access token expires after 15 minutes** (not 1 minute)
2. **Silent refresh mechanism** works seamlessly
3. **Login/logout functionality** as expected
4. **Core order management features**
5. **Database connectivity** and configuration
6. **Frontend UI responsiveness**

---

## 📋 Final Checklist

- [ ] Build completed without errors
- [ ] Access token lifetime verified (15 minutes)
- [ ] Package created with checksum
- [ ] QA environment backed up
- [ ] Service installed/updated successfully
- [ ] Database configured and tested
- [ ] Health endpoints returning 200 OK
- [ ] SPA loading correctly
- [ ] QA team notified of deployment
- [ ] QA release checklist documented

---

## Shell Variants

### Git Bash / WSL / macOS / Linux

If you are **not** using Windows PowerShell, use the original Unix/Linux commands shown below for **Steps 1–5 only**. Steps 7 onwards are PowerShell (QA server is Windows).

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

---
