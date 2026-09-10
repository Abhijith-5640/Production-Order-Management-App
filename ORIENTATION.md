# NexusProd - Developer Orientation Guide

## Overview

**NexusProd** is a full-stack Production Order Management Portal for tracking daily production distributions across multiple shifts (trips) and facility sections.

| Layer | Technology |
|-------|------------|
| Frontend | React 19, Vite, Tailwind CSS v4 |
| Backend | .NET 8 API (ASP.NET Core Minimal API) |
| Database | MySQL |
| Auth | JWT (Access Token + Refresh Token) |

---

## Prerequisites

- **.NET 8 SDK** ([Download](https://dotnet.microsoft.com/download/dotnet/8.0))
- **Node.js 18+** ([Download](https://nodejs.org/))
- **MySQL Server** (Running locally or accessible via network)
- **Git** (for cloning)

---

## Project Structure

```
Production-Order-Management-App/
├── client/                     # React SPA
│   ├── src/
│   │   ├── components/         # Reusable UI components
│   │   ├── pages/              # App routes (Login, Dashboard)
│   │   ├── services/           # API calls (api.js)
│   │   └── index.css          # Tailwind entry point
│   └── package.json
│
├── src/
│   ├── NexusProd.Api/          # .NET 8 API
│   │   ├── Api/               # Endpoints (HTTP routes)
│   │   ├── Application/        # Business logic, abstractions
│   │   ├── Domain/             # Entities
│   │   ├── Infrastructure/    # DB repos, JWT, config
│   │   ├── Updater/           # Auto-update service
│   │   ├── wwwroot/           # Built SPA (Vite output)
│   │   └── appsettings.json   # Server config
│   │
│   └── NexusProd.Updater.Helper/  # Launcher for auto-updates
│
├── MySQL_Assets/
│   └── prod_app_db_meta_data.sql  # Database schema
│
└── package.json                # Root npm scripts
```

---

## Local Setup

### 1. Clone & Install

```bash
# Clone the repo
git clone <repo-url>
cd Production-Order-Management-App

# Install client dependencies
npm run install:client

# Restore .NET packages
dotnet restore
```

### 2. Database Setup

```bash
# Create database and tables (run once)
mysql -u root -p < MySQL_Assets/prod_app_db_meta_data.sql
```

### 3. Configure API

Create or edit `src/NexusProd.Api/db_config.json`:

```json
{
  "host": "localhost",
  "port": 3306,
  "user": "root",
  "password": "YOUR_PASSWORD",
  "database": "prod_app"
}
```

### 4. Run Development Servers

Open **two terminals**:

```bash
# Terminal 1: API (port 5099)
npm run dev:api

# Terminal 2: Client (port 5173)
npm run dev:client
```

- API: `http://localhost:5099`
- Client: `http://localhost:5173`

> **Note:** The Vite dev proxy is configured to forward `/api` requests to port 5099 automatically.

---

## Available npm Scripts

From root directory:

| Script | Description |
|--------|-------------|
| `npm run install:client` | Install client dependencies |
| `npm run build:client` | Build React SPA to `src/NexusProd.Api/wwwroot` |
| `npm run dev:client` | Start Vite dev server |
| `npm run dev:api` | Start .NET API in development mode |
| `npm run publish:full` | Full build + publish (client + API + updater) |

---

## Building for Production

```bash
# Full production build (runs automatically during publish)
npm run publish:full
```

Output location:
```
src/NexusProd.Api/bin/Release/net8.0/win-x64/publish/
├── NexusProd.Api.exe      # API executable
├── NexusProd.exe          # Launcher
├── wwwroot/               # Compiled React SPA
├── version.json           # Current version
└── nexusprod-qa-*.zip     # Deployable package
```

---

## Configuration Files

| File | Purpose |
|------|---------|
| `appsettings.json` | API config (port, JWT, logging) |
| `appsettings.Development.json` | Dev overrides |
| `db_config.json` | Database connection |
| `wwwroot/config.json` | Client → API URL |

### Changing API Port

Edit `appsettings.json`:
```json
"Kestrel": {
  "Endpoints": {
    "Http": {
      "Url": "http://0.0.0.0:8443"
    }
  }
}
```

### Changing Client API URL

Edit `wwwroot/config.json`:
```json
{
  "API_BASE_URL": "http://localhost:8443/api"
}
```

---

## Auto-Update System

The app has a built-in auto-update mechanism:

1. **NexusProd.exe** - Launcher that monitors for updates
2. **NexusProd.Api.exe** - Main API
3. **Updater service** - Polls update server every 5 minutes (configurable)

### Update Flow
1. Deploy `update-pending.zip` to update server
2. Launcher detects new version
3. Extracts and applies update atomically
4. API restarts with new version

---

## Process Management

```powershell
# Start
Start-Process -FilePath "C:\Works\POS\NexusProd\NexusProd.exe"

# Stop
Stop-Process -Name "NexusProd,NexusProd.Api" -Force

# Check Status
Get-Process -Name "*NexusProd*"

# Auto-start on Windows login
Set-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" `
    -Name "NexusProd" `
    -Value "C:\Works\POS\NexusProd\NexusProd.exe"
```

---

## Key API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/health` | Health check |
| POST | `/api/auth/login` | Login |
| GET | `/api/sections` | Get sections |
| GET | `/api/trips` | Get trips |
| GET | `/api/orders` | Get orders |
| PUT | `/api/invoices/{id}` | Update invoice |

Full documentation: [API_DOCUMENTATION.md](API_DOCUMENTATION.md)

---

## Application Workflow

1. **Login** → Authenticate with username/password
2. **Generate Invoices** → Create daily production orders
3. **Select Section** → Choose production area (e.g., Fresh Bakery)
4. **Select Trip** → Choose shift/time (e.g., 06:00 AM)
5. **Verify Items** → Update quantities per branch
6. **Exclude/Route** → Handle excluded items, roll to next trip
7. **Complete** → Mark items done, auto-sorts to bottom

---

## Troubleshooting

| Issue | Solution |
|-------|----------|
| "Connection failed" | Check `db_config.json` credentials |
| "Client can't reach API" | Verify `wwwroot/config.json` API URL |
| "Port already in use" | Stop existing process or change port |
| Empty data | Check database has orders for today |
| Https redirection warning | Ignore if using HTTP (not an error) |

---

## Version

Current version: **1.0.6** (defined in `package.json`)

Update version before publishing:
```bash
# Edit package.json
# Then run publish
npm run publish:full
```

---

## Support

- API Logs: `logs/api.log`
- Launcher Logs: `logs/launcher.log`
- Crash Logs: `logs/crash-count.txt`
