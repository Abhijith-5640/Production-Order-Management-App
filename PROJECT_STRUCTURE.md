# Production Order Management App — Project Structure

A full-stack production-load management portal: React 19 SPA on the front, ASP.NET Core 8 minimal API on the back, MySQL as the data store, packaged as a single Windows executable.

The legacy Node/Express backend (`server/`, `scripts/`, root `package.json`) is **not** part of the current solution and is ignored throughout this document. The current backend is `src/NexusProd.Api/`.

---

## High-level architecture

```
                ┌──────────────────────────────┐
                │  Browser (React 19 + Vite)   │
                │  client/src/                 │
                └──────────────┬───────────────┘
                               │ HTTP (fetch)
                               │ Bearer access JWT in Authorization header
                               │ Refresh JWT in httpOnly cookie
                               ▼
   ┌─────────────────────────────────────────────────────────────┐
   │  ASP.NET Core 8 minimal API  (src/NexusProd.Api)           │
   │  ───────────────────────────────────────────────────────   │
   │  Api/Endpoints/*    — thin HTTP layer (routes, contracts)   │
   │  Application/       — use-case handlers, Result<T>, Error   │
   │  Domain/            — entities (User, OrderItem, …)         │
   │  Infrastructure/    — Dapper repos, JWT, file config store  │
   │  Updater/           — in-app self-update                    │
   │  wwwroot/           — Vite production build, served as SPA  │
   └────────────┬──────────────────────────────────────┬─────────┘
                │                                      │
                ▼                                      ▼
       ┌──────────────────┐                 ┌──────────────────┐
       │ MySQL            │                 │ db_config.json   │
       │ trader_sm_qa     │                 │ (editable, ships │
       │ (Dapper)         │                 │  next to .exe)   │
       └──────────────────┘                 └──────────────────┘
```

- **One process.** The .NET API serves both the REST API (`/api/*`) and the React SPA (`/`, `/assets/*`). The SPA build output is copied into `wwwroot/` by an MSBuild target that runs `npm --prefix client run build` before every `dotnet build`.
- **One config file the user can edit.** `db_config.json` lives next to the executable. `FileDbConfigStore` reads/writes it; `SaveConfigHandler` exposes it over HTTP.
- **In-app updater.** A separate `Updater/` subsystem polls an HTTP server, downloads a zip, and applies it in place. Run by the configured Windows service.

---

## Top-level layout

```
Production-Order-Management-App/
├── .vscode/
│   ├── launch.json         # ".NET API (Debug)", "Vite Dev Server", "Debug Client (Chrome)", compounds
│   └── tasks.json          # build-api, build-client, dev-client, watch-api
│
├── MySQL_Assets/
│   └── prod_app_db_meta_data.sql    # reference schema (demo). Live DB is `trader_sm_qa`.
│
├── client/                 # React 19 + Vite 7 SPA
│
├── src/NexusProd.Api/      # .NET 8 single-file publish target
│
├── .gitignore              # ignores node_modules, dist, bin, obj
└── (server/, scripts/, root package.json)   # legacy, NOT in current solution
```

---

## `client/` — React 19 + Vite 7 SPA

| Path | Purpose |
|---|---|
| `client/index.html` | Vite entry; the only thing served by Vite in dev. |
| `client/vite.config.js` | React + Tailwind v4 plugins, dev port `5173`, `/api` proxy → `http://127.0.0.1:5099`, `build.outDir: '../src/NexusProd.Api/wwwroot'`. |
| `client/package.json` | `react@19`, `react-dom@19`, `react-router-dom@7`, `tailwindcss@4`, `lucide-react`, `react-toastify`. Scripts: `dev`, `build`, `lint`, `preview`. |
| `client/src/main.jsx` | React 19 `createRoot` bootstrap. |
| `client/src/App.jsx` | `BrowserRouter` + routes: `/login` (public) and `/` (private, gated by `localStorage.nexus_authenticated`). |
| `client/src/index.css` | Tailwind v4 entry (`@import "tailwindcss";`). |
| `client/src/App.css` | Component-scoped CSS. |
| `client/src/services/api.js` | Single `api` object; every method maps 1:1 to a .NET endpoint. Picks `http://localhost:5099/api` in dev, `/api` in prod. Reads the access JWT from `localStorage.nexus_token` and re-attaches it to every call. |
| `client/src/pages/Login.jsx` | Username/password form, calls `api.login`, stores the access token + `nexus_authenticated=true`. |
| `client/src/pages/Dashboard.jsx` | Main app: section + trip pickers, order list, "Pending Orders / Generate" action, detail/exclude modals. |
| `client/src/components/DetailModal.jsx` | Per-item distribution editor (qty per branch). |
| `client/src/components/PickerModal.jsx` | Section + trip selection modal. |
| `client/src/components/FullScreenLoader.jsx` | Reusable overlay. |
| `client/src/assets/` | `react.svg`, Vite-served static assets. |

**Data flow at a glance**
1. Login → `api.login` → `/api/auth/login` → access token stored in `localStorage`, `nexus_authenticated=true`.
2. Dashboard mount → `api.getSections()` → `/api/sections` → list of sections.
3. Section picked → `api.getTrips(section)` → `/api/trips?section=…` → list of trips.
4. Trip picked → `api.getOrders(section, trip)` → `/api/orders?section=…&trip=…` → order list.
5. Edit qty → `api.updateOrder(...)` → `POST /api/orders/update`.
6. Exclude branch/item → `api.excludeItem(...)` → `POST /api/orders/exclude`.
7. Generate invoices → `api.generateInvoices(userId)` → `POST /api/orders/generate`.
8. Logout → `api.logout()` → `POST /api/auth/logout`, clears localStorage, navigates to `/login`.

---

## `src/NexusProd.Api/` — ASP.NET Core 8 minimal API

| Path | Purpose |
|---|---|
| `Program.cs` | Composition root. Configures logging, settings, `AddApplication` / `AddInfrastructure` / `AddUpdater`, JWT bearer auth, CORS (loopback only), problem-details exception handler, static files, auth middleware, `Map*Endpoints`, SPA fallback to `index.html`. |
| `NexusProd.Api.csproj` | `net8.0`, `win-x64`, self-contained, single-file publish. Refs: Dapper, MySqlConnector, BCrypt.Net-Next, JwtBearer, Swashbuckle, Serilog (console + file), System.IdentityModel.Tokens.Jwt. **MSBuild target** `NpmBuildClient` runs `npm --prefix ..\..\client run build` before `BeforeBuild`; `CopySpaToOutput` mirrors `wwwroot/` into `bin/Debug/.../wwwroot/` so `dotnet build` (not just `dotnet publish`) is runnable. |
| `appsettings.json`, `appsettings.Development.json` | `JwtSettings`, `UpdateServerSettings`, `Serilog`, default Kestrel URL `http://0.0.0.0:5099`. |
| `Resources/default_db_config.json` | Embedded + copied to output. The seed config used on first run when no `db_config.json` exists next to the binary. |
| `globalusings.cs` | Project-wide `using` aliases. |
| `wwwroot/` | Vite build output. In source: `index.html` + `assets/`. The MSBuild target refreshes these on every build. |

### `Api/` — HTTP layer

| Path | Purpose |
|---|---|
| `Api/Endpoints/AuthEndpoints.cs` | `/api/auth/login` (anon), `/api/auth/refresh` (anon, reads cookie), `/api/auth/logout`, `/api/auth/me` (authenticated). Sets the refresh JWT in an `httpOnly` cookie. |
| `Api/Endpoints/OrderEndpoints.cs` | `GET /api/orders/check-pending`, `POST /api/orders/generate`, `GET /api/orders?section=&trip=`, `POST /api/orders/update`, `POST /api/orders/exclude`. All under `RequireAuthorization("AuthenticatedUser")`. |
| `Api/Endpoints/LookupEndpoints.cs` | `GET /api/sections`, `GET /api/trips?section=…`, `GET /api/server-info`. |
| `Api/Endpoints/ConfigEndpoints.cs` | `POST /api/config/save`, `POST /api/config/test`. The path that lets the user edit `db_config.json` from the UI. |
| `Api/Endpoints/UpdaterEndpoints.cs` | `POST /api/updater/check`, `GET /api/updater/status`. |
| `Api/Endpoints/ResultExtensions.cs` | `Result<T>` → `IResult` mapper. Translates the typed `Error` union into the right HTTP status + JSON. |
| `Api/Contracts/*.cs` | Request/response DTOs (records). One file per resource family: `Auth`, `Order`, `Lookup`, `Config`. |
| `Api/Mappers/OrderMapper.cs` | Domain entity → DTO conversion for orders. |
| `Api/Filters/ProblemDetailsExceptionHandler.cs` | `IExceptionHandler` — last-resort 500 → RFC 7807 `application/problem+json`. |
| `Api/Middleware/JwtBlacklistMiddleware.cs` | Rejects access tokens whose JTI is in the in-memory blacklist. |

### `Application/` — Use cases (handlers)

`IHandler<TRequest, TResponse>` is the only abstraction a handler needs. Handlers return `Result<TResponse>`; a typed `Error` union describes success/failure reasons and drives the HTTP status mapping in the endpoint.

| Path | Purpose |
|---|---|
| `Application/Common/IHandler.cs` | `IHandler<TRequest, TResponse>` + `IRequest<TResponse>`. |
| `Application/Common/Result.cs` | `Result<T>` discriminated union + `Ok` / `Fail` factories. |
| `Application/Common/Error.cs` | Typed `Error` (Unauthorized, InvalidInput, DatabaseError, ConfigurationError, NotFound, …). |
| `Application/Common/ValidationException.cs` | Thrown by handlers/validators; caught by the global exception handler. |
| `Application/DependencyInjection.cs` | One place to register every handler. `AddApplication()`. |
| `Application/UseCases/Auth/` | `LoginHandler`, `RefreshHandler`, `LogoutHandler`, `GetCurrentUserHandler`. |
| `Application/UseCases/Orders/` | `CheckPendingHandler`, `GenerateInvoicesHandler`, `GetOrdersHandler`, `UpdateInvoiceHandler`, `ExcludeItemHandler`. |
| `Application/UseCases/Lookups/` | `GetSectionsHandler`, `GetTripsHandler`, `GetServerInfoHandler`. |
| `Application/UseCases/Config/` | `SaveConfigHandler`, `TestDbHandler`, `CheckUpdateHandler`, `GetUpdateStatusHandler`. |
| `Application/Abstractions/` | Interfaces only: `IUserRepository`, `IOrderRepository`, `IUnitOfWork`, `IJwtTokenService`, `IRefreshTokenStore`, `IPasswordHasher`, `IClock`, `IDbConfigStore`, `IUpdateServer`, `IUpdateInstaller`, `IUpdateState`, `IUpdateTrigger`. |

**Why a use-case layer?** Handlers stay decoupled from HTTP. The same `LoginHandler` could be invoked from a console tool, a background job, or a test fixture without touching ASP.NET. They also return `Result<T>`, so success/failure is *typed* — there are no thrown exceptions for expected failures.

### `Domain/` — Entities and value objects

| Path | Purpose |
|---|---|
| `Domain/Entities/User.cs` | `Id`, `UserName`, `PasswordHash`, `IsActive`, `DefaultBranchId`. |
| `Domain/Entities/OrderItem.cs` | Item with `Distribution: List<DistributionEntry>`, `IsCompleted`. |
| `Domain/Entities/DistributionEntry.cs` | `Branch`, `Trip`, `Qty`. |
| `Domain/Entities/Section.cs`, `Domain/Entities/Trip.cs` | Lookup entities. |
| `Domain/ValueObjects/HashedPassword.cs` | Wrapper around a bcrypt hash string. |

### `Infrastructure/` — Adapters

| Path | Purpose |
|---|---|
| `Infrastructure/DependencyInjection.cs` | `AddInfrastructure(IConfiguration)`. Registers `MySqlConnectionFactory`, repositories, `BcryptPasswordHasher`, `JwtTokenService`, the in-memory refresh + access-blacklist stores, `FileDbConfigStore`, `SystemClock`, and the placeholder `NullUpdateInstaller`. |
| `Infrastructure/Configuration/Settings.cs` | `JwtSettings`, `UpdateServerSettings`. |
| `Infrastructure/Persistence/MySqlConnectionFactory.cs` | Opens a `MySqlConnection` using the active `DbConfig` (read by `FileDbConfigStore`). |
| `Infrastructure/Persistence/MySqlUserRepository.cs` | Dapper-based user lookup + bcrypt verify. |
| `Infrastructure/Persistence/MySqlOrderRepository.cs` | Dapper-based reads + transactional writes: `CheckPendingOrdersAsync`, `GenerateInvoicesAsync`, `GetSectionsAsync`, `GetTripsAsync`, `GetOrdersAsync`, `UpdateInvoiceAsync`, `ExcludeItemAsync`. All methods wrap the Dapper call in `try/catch` and log via injected `ILogger<T>`. |
| `Infrastructure/Persistence/MySqlUnitOfWork.cs` | Unit-of-work façade over the connection factory. |
| `Infrastructure/Persistence/FileDbConfigStore.cs` | Reads/writes the editable `db_config.json` next to the binary. Falls back to the embedded `Resources/default_db_config.json` on first run. |
| `Infrastructure/Security/BcryptPasswordHasher.cs` | `IPasswordHasher` over `BCrypt.Net-Next`. |
| `Infrastructure/Security/JwtTokenService.cs` | Issues + validates HS256 access + refresh tokens; registered as both concrete and `IJwtTokenService`. |
| `Infrastructure/Security/InMemoryRefreshTokenStore.cs` | `IRefreshTokenStore`: stores JTIs → userId, supports revoke and lookup. |
| `Infrastructure/Security/InMemoryAccessTokenBlacklist.cs` | `IAccessTokenBlacklist`: revoked JTIs are checked by `JwtBlacklistMiddleware`. |
| `Infrastructure/Time/SystemClock.cs` | `IClock` implementation returning `DateTimeOffset.UtcNow`. |

### `Updater/` — Self-update subsystem

| Path | Purpose |
|---|---|
| `Updater/AppUpdater.cs` | Coordinates: poll `IUpdateServer`, compare versions, trigger `IUpdateInstaller` on a new release. |
| `Updater/HttpUpdateServer.cs` | `HttpClient`-backed check + download of a versioned manifest and zip. |
| `Updater/FileSystemUpdateInstaller.cs` | Applies a downloaded zip in place (the "real" installer, wired in after install dir is known). |
| `Updater/DependencyInjection.cs` | `AddUpdater()` composition. |

---

## Request/response shape

Every endpoint returns the same envelope on failure:

```json
{ "success": false, "message": "Database connection refused" }
```

…and a typed DTO on success. `ResultExtensions.ToHttp` is the single point that maps `Error.Kind` → HTTP status (e.g. `Unauthorized` → 401, `InvalidInput` → 400, `DatabaseError` → 500, `ConfigurationError` → 500). `ProblemDetailsExceptionHandler` only fires for uncaught exceptions.

---

## Cross-cutting middleware order (from `Program.cs`)

```
1. UseExceptionHandler        — global try/catch → RFC 7807
2. UseHttpsRedirection       — only outside Development
3. UseDefaultFiles           — serves /index.html at /
4. UseStaticFiles            — serves /assets/* from wwwroot/
5. UseCors                   — loopback only
6. UseRouting
7. UseAuthentication         — JWT bearer
8. JwtBlacklistMiddleware    — 401 if access JTI is revoked
9. UseAuthorization
10. Map*Endpoints            — /api/auth, /api/orders, /api/lookups, /api/config, /api/updater
11. MapFallbackToFile("index.html")   — SPA client-side routing works on refresh
```

---

## VS Code launch / tasks

- `.vscode/tasks.json` — `build-api` (`dotnet build` with `SkipNpmBuild=true`), `build-client`, `dev-client` (`npm run dev`), `watch-api`.
- `.vscode/launch.json` — three single configs:
  - **.NET API (Debug)** — runs the built DLL, `ASPNETCORE_URLS=http://127.0.0.1:5099`.
  - **Vite Dev Server** — `npm --prefix client run dev -- --host` (port `5173`).
  - **Debug Client (Chrome)** — opens Chrome at `http://localhost:5173` with `webRoot` set to `client/src`.
  - Compounds: **Full Stack (API + Vite)** and **Full Stack (API + Chrome)**.

---

## Build & run cheatsheet

```bash
# Build everything (the .csproj triggers `npm run build` automatically)
dotnet build src/NexusProd.Api/NexusProd.Api.csproj

# Run the API
dotnet run --project src/NexusProd.Api
# → API on http://127.0.0.1:5099 ; SPA at /

# Run the SPA in dev mode (with the /api proxy → :5099)
npm --prefix client run dev
# → Vite on http://localhost:5173

# Publish a single-file Windows executable
dotnet publish src/NexusProd.Api -c Release -r win-x64
# → src/NexusProd.Api/bin/Release/net8.0/win-x64/publish/NexusProd.Api.exe
```

---

## What is intentionally not in this layout

- `server/`, `scripts/`, root `package.json` — legacy Express/node packaging. Replaced by `src/NexusProd.Api/`. Still present in the tree but not referenced by any current build or run path.
- `MySQL_Assets/prod_app_db_meta_data.sql` — reference/demo schema. The live database is `trader_sm_qa`.
- `wwwroot/` source contents — generated, never hand-edited.