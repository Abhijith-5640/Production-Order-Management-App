# QA Release Checklist

**Project:** NexusProd — Production Order Management App  
**Environment:** QA / Staging  
**Date:** _______________  
**Build Version:** _______________  
**Built By:** _______________  
**QA Lead:** _______________  

---

## Scope Notes

- **Access Token Lifetime:** Changed to **15 minutes** (was 1 min in dev, now 15 min in appsettings.json).
- **Refresh Token Lifetime:** **Not changed** — remains 7 days (`RefreshTokenLifetimeDays` in `Settings.cs`).
- **Updater Functionality:** **Excluded from QA scope** — do not test auto-update or manual update endpoints in this release. Updater endpoints (`/api/updater/*`) are present in the binary but marked out of scope.

---

## 1. Build Verification

- [ ] `npm run build --prefix client` completes without errors
- [ ] `dotnet publish src/NexusProd.Api -c Release -r win-x64` completes without errors
- [ ] `appsettings.json` confirms `AccessTokenLifetimeMinutes` = **15**
- [ ] `appsettings.local.json` is present on the target machine (JWT secrets preserved)
- [ ] No uncommitted secrets leaked in git diff / build output
- [ ] Build output directory is clean (no stale `wwwroot/` from previous builds)

## 2. Deployment Pre-checks

- [ ] Existing service (if upgrading) is stopped before deploy
- [ ] Target machine has .NET 8 Runtime (or uses self-contained binary)
- [ ] `db_config.json` is present in the same directory as the executable
- [ ] MySQL server is reachable from the target machine
- [ ] Target database (`prod_app`) exists and is accessible
- [ ] Firewall rule / port 8443 is open on the target machine
- [ ] Port 8443 is not already in use by another process

## 3. Core Authentication (Access Token — 15 min)

- [ ] **Login** — Valid credentials return access token + refresh cookie (`rt`)
- [ ] **Access token in localStorage** — Token stored as `nexus_token`
- [ ] **Token expiry tracked** — `nexus_token_expires` recorded in localStorage (~15 min ahead)
- [ ] **Authenticated requests succeed** — Dashboard loads after login
- [ ] **Access token expires** — After 15 minutes, any protected API call returns `{"error":"token_expired",...}`
- [ ] **Silent refresh works** — On `token_expired`, client calls `/api/auth/refresh` automatically, gets new access token, retries the original request
- [ ] **Multiple concurrent requests during expiry** — All share the same in-flight refresh promise; no duplicate refresh calls
- [ ] **Hard logout on other 401** — Non-expiry 401 clears localStorage and redirects to `/login`
- [ ] **Logout** — `/api/auth/logout` revokes JTI + blacklists access token + deletes `rt` cookie
- [ ] **Post-logout requests rejected** — Any API call after logout returns 401 immediately

## 4. Core Authentication (Refresh Token — 7 days, not changed)

- [ ] **Refresh rotation** — Each `/api/auth/refresh` call issues a new refresh token (new `rt` cookie)
- [ ] **Old refresh token superseded** — Previously issued refresh token is rejected after rotation
- [ ] **Concurrent refresh race** — Two simultaneous refresh calls within grace window (30 sec) both succeed
- [ ] **Stale cookie after rotation** — Client retries once with updated cookie on `token_already_rotated`
- [ ] **Refresh endpoint reachable** — `/api/auth/refresh` (POST, anonymous) returns 200 with new tokens

## 5. User & Session Management

- [ ] **`/api/auth/me`** returns correct user info for authenticated session
- [ ] **Session persists across page reloads** (refresh token via HttpOnly cookie)
- [ ] **Cold-start refresh** — Page loaded with expired access token but valid refresh cookie triggers silent refresh before rendering
- [ ] **`nexus:session_expired` event** — Dispatched and listened to by `SessionExpiredBridge`, navigates to `/login`

## 6. Core API Functionality

- [ ] **Orders list** — `/api/orders` (or equivalent) returns expected data
- [ ] **Section / Trip lookups** — Dropdowns populated correctly
- [ ] **Invoice generation** — End-to-end: generate invoice, verify in DB
- [ ] **Invoice update** — Existing invoice can be updated
- [ ] **Item exclusion** — Items can be excluded from orders
- [ ] **Pending orders check** — Pending orders detected and displayed

## 7. Configuration API

- [ ] **Settings page** — DB connection settings save correctly
- [ ] **Test DB connection** — `/api/config/test-db` validates connection
- [ ] **`db_config.json`** — Written correctly to disk after saving settings

## 8. Frontend / SPA

- [ ] **SPA builds and loads** — No 404s on route navigation
- [ ] **Login page renders** — All form fields visible and functional
- [ ] **Dashboard renders** — All sections, trips, counters populated
- [ ] **Modals** — Detail, Adjustment, Picker, QtyInput modals open/close correctly
- [ ] **Full-screen loader** — Appears during initial API calls, dismisses on response
- [ ] **Toast notifications** — Success/error toasts display correctly
- [ ] **Private route guard** — Unauthenticated users redirected to `/login`

## 9. Networking & CORS

- [ ] **CORS** — API accepts requests from the client origin
- [ ] **Credentials** — `credentials: 'include'` works; refresh cookie sent with requests
- [ ] **HTTPS/HTTP** — Cookie `Secure` flag matches deployment protocol
- [ ] **Proxy** — If using a reverse proxy, `X-Forwarded-*` headers forwarded correctly

## 10. Out of Scope — Updater (SKIPPED)

> The following updater-related items are **explicitly skipped** for this QA cycle:

- [ ] ~~Auto-update polling~~ — Skipped
- [ ] ~~Manual update check (`/api/updater/check`)~~ — Skipped
- [ ] ~~Update status endpoint (`/api/updater/status`)~~ — Skipped
- [ ] ~~`AppUpdater` background service~~ — Skipped
- [ ] ~~`FileSystemUpdateInstaller` (WinSW service restart + zip extract)~~ — Skipped

> **Note:** Updater endpoints remain reachable in the binary but are not exercised. The `UpdateServerSettings.Enabled = false` default ensures the background service is dormant.

## 11. Security Smoke Tests

- [ ] **Rate limiting on login** — 6 rapid login attempts from same IP returns 429
- [ ] **Protected endpoint without token** — Returns 401 (not 200)
- [ ] **Tampered access token** — Returns 401
- [ ] **Expired refresh token** — Returns 401 + redirects to login
- [ ] **HttpOnly cookie** — `rt` cookie is not accessible via `document.cookie`

## 12. Performance

- [ ] **Login response time** — < 2 seconds
- [ ] **Dashboard loads** — < 3 seconds (including silent refresh if needed)
- [ ] **API endpoints** — All respond within acceptable SLA (< 1s)

## 13. Regression — Previously Broken Scenarios

- [ ] *(List any specific regressions from prior builds that need re-verification)*
  - ___________________________________________________________________
  - ___________________________________________________________________

---

## Sign-off

| Role | Name | Date | Signature |
|---|---|---|---|
| Build Engineer | | | |
| QA Lead | | | |
| Project Manager | | | |

---

## Build Artefacts

| File | Path | SHA-256 |
|---|---|---|
| API Executable | | |
| SPA (wwwroot/) | | |
| `db_config.json` (if separate) | | |
| `appsettings.local.json` | | |
