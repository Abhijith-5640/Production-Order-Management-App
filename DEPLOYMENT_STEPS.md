# 🚀 NexusProd Build & Deployment Guide for QA

This document provides clear, step-by-step instructions for building and deploying the NexusProd application to QA environment.

---

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Build Steps](#build-steps)
3. [Package Artefacts](#package-artefacts)
4. [QA Environment Deployment](#qa-environment-deployment)
5. [Post-Deployment Verification](#post-deployment-verification)
6. [Rollback Plan](#rollback-plan)
7. [Important Notes](#important-notes)

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

```bash
# Navigate to project root
cd "c:\Dev Works\Web POS\Production-Order-Management-App"

# Clean any previous build outputs
rm -rf src/NexusProd.Api/bin/Release/
rm -rf src/NexusProd.Api/obj/
rm -rf client/node_modules/.cache

# Install dependencies (only if changed)
npm install --prefix client
```

### Step 2: Verify Configuration

**Check JWT Settings:**

```bash
# Ensure access token lifetime is 15 minutes (not 1 minute)
grep -n "AccessTokenLifetimeMinutes" src/NexusProd.Api/appsettings.json
# Expected: "AccessTokenLifetimeMinutes": 15,
```

**Key Configuration Values:**
- ✅ Access Token Lifetime: **15 minutes**
- ❌ Refresh Token Lifetime: **7 days** (unchanged)
- ❌ Updater: **Disabled** by default

### Step 3: Build Client (SPA)

```bash
# Build the React application
npm run build --prefix client

# Verify output directory exists
ls -la src/NexusProd.Api/wwwroot/
```

**Expected Output:**
- `src/NexusProd.Api/wwwroot/index.html`
- `src/NexusProd.Api/wwwroot/assets/`
- `src/NexusProd.Api/wwwroot/favicon.svg`

### Step 4: Publish API

```bash
# Publish the .NET application
npm run publish:api

# Verify published files
ls -la src/NexusProd.Api/bin/Release/net8.0/win-x64/publish/
```

**Expected Files in Publish Directory:**
```
📁 src/NexusProd.Api/bin/Release/net8.0/win-x64/publish/
├── 📄 NexusProd.Api.exe              # Self-contained executable
├── 📄 appsettings.json               # Main config
├── 📄 appsettings.Development.json    # Dev config
├── 📄 appsettings.local.json         # Local secrets (preserved)
├── 📁 wwwroot/                       # React SPA
├── 📁 Resources/                    # Embedded resources
└── 📄 *.config                       .NET config files
```

---

## Package Artefacts

### Step 5: Prepare Deployment Package

```bash
# Navigate to publish directory
cd src/NexusProd.Api/bin/Release/net8.0/win-x64/publish/

# Create timestamped ZIP
TIMESTAMP=$(date +"%Y%m%d-%H%M%S")
zip -r nexusprod-qa-${TIMESTAMP}.zip .

# Generate SHA-256 checksum
sha256sum nexusprod-qa-${TIMESTAMP}.zip > nexusprod-qa-${TIMESTAMP}.zip.sha256

# List files in package
ls -lah nexusprod-qa-${TIMESTAMP}.zip*
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
# Connect to QA server via SSH or use PowerShell remoting

# Stop existing service
Stop-Service -Name NexusProd

# Backup current installation
$date = Get-Date -Format "yyyyMMdd-HHmmss"
Move-Item -Path "C:\Program Files\NexusProd" -Destination "C:\Program Files\NexusProd-Bak-$date"

# Verify backup exists
Get-ChildItem "C:\Program Files\NexusProd-Bak-$date"
```

### Step 8: Deploy New Version

```powershell
# Copy the ZIP file to QA server
scp nexusprod-qa-${TIMESTAMP}..zip qa-server:C:\temp\

# On QA server (via PowerShell):
cd C:\Program Files

# Extract new version
Expand-Archive -Path "C:\temp\nexusprod-qa-${TIMESTAMP}.zip" -DestinationPath "NexusProd" -Force

# Verify key files exist
Get-ChildItem "NexusProd" | Select-Object Name, LastWriteTime
```

### Step 9: Install/Update Service

If this is a **new installation**:

```powershell
# Download WinSW if not present
Invoke-WebRequest -Uri "https://github.com/winsw/winsw/releases/latest/download/WinSW-x64.exe" -OutFile "C:\Program Files\NexusProd\WinSW-x64.exe"

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
"@ | Out-File -FilePath "C:\Program Files\NexusProd\NexusProd.Api.xml" -Encoding UTF8
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
Stop-Service -Name NexusProd

# Remove failed installation
Remove-Item -Path "C:\Program Files\NexusProd" -Recurse -Force

# Restore from backup
Move-Item -Path "C:\Program Files\NexusProd-Bak-$date" -Destination "C:\Program Files\NexusProd"

# Start old version
Start-Service -Name NexusProd
```

### Quick Rollback Commands

```powershell
# One-liner rollback (when you have timestamp)
$timestamp = "20240101-120000"
Stop-Service NexusProd
Remove-Item C:\Program Files\NexusProd -Recurse -Force
Move-Item "C:\Program Files\NexusProd-Bak-$timestamp" C:\Program Files\NexusProd
Start-Service NexusProd
```

---

## Important Notes

### 1. Access Token Lifetime Changes
- **Changed**: 1 minute → **15 minutes**
- **Purpose**: Allow longer sessions without frequent refreshes
- **QA Focus**: Verify silent refresh works within 15 minutes

### 2. Updater Functionality (SKIPPED in QA)
- ❌ Do not test auto-update features
- ❌ Do not test `/api/updater/*` endpoints
- ❌ The updater service is disabled by default

### 3. File Locations
- **Executable**: `C:\Program Files\NexusProd\NexusProd.Api.exe`
- **Config**: `C:\Program Files\NexusProd\db_config.json`
- **Logs**: `C:\Program Files\NexusProd\logs\`
- **WinSW**: `C:\Program Files\NexusProd\WinSW-x64.exe`

### 4. Common Issues

| Symptom | Solution |
|---------|----------|
| Service starts then stops | Check logs: `C:\Program Files\NexusProd\logs\` |
| Port 8443 already in use | Find process: `netstat -ano \| findstr 8443` |
| API health fails | Check MySQL connection, firewall, permissions |
| SPA not loading | Verify wwwroot directory exists and has files |

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

*Note: Always test in staging environment before production deployment.*