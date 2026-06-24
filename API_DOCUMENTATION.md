# Nexus Prod — API Documentation

This document describes the REST endpoints exposed by the **Nexus Prod** server (ASP.NET Core 8, minimal API). It documents the request/response shape for every public route group.

## Conventions

- **Base URL**: `http://localhost:5099/api` during development. In production the host is whatever the Windows service is bound to — see [DEPLOYMENT_GUIDE.md](DEPLOYMENT_GUIDE.md).
- **Auth**: most `/api/orders/*` endpoints require a JWT bearer token in the `Authorization: Bearer <accessToken>` header. The token is short-lived (15 min) and is re-issued silently via `POST /api/auth/refresh` using the HttpOnly refresh cookie set at login.
- **Errors**: failures return `application/problem+json` with a `ProblemDetails` body. Domain failures from use cases return HTTP 400 with a `{ "message": "...", "code": "InvalidInput" }` body. Unhandled exceptions become HTTP 500.
- **Content type**: all request bodies are `application/json` unless noted.

---

## Auth — `/api/auth`

JWT issuance, refresh, logout, and "who am I".

### `POST /api/auth/login`

Validates credentials against `user_master` and returns a fresh access token. Sets the refresh token in an HttpOnly cookie (`Secure` flag is set when the request is HTTPS, off on plain HTTP for local-network use).

**Request**
```json
{ "username": "admin", "password": "12345" }
```

**Response 200**
```json
{
  "accessToken": "eyJhbGciOi...",
  "accessExpiresAt": "2026-06-24T10:30:00+00:00",
  "user": "admin",
  "userId": 1,
  "userBrnchId": 1,
  "userCounterId": 1
}
```

**Response 400** — invalid credentials
```json
{ "message": "Invalid username or password", "code": "InvalidInput" }
```

**Side effect**: response sets a cookie named `nexusprod_rt` (name configurable via `JwtSettings:CookieName`).

### `POST /api/auth/refresh`

Reads the refresh cookie, validates the JWT, rotates its JTI server-side, and returns a new short-lived access token. Does **not** rotate the cookie itself — clients in the silent-refresh scenario only need a new access token.

**Request**: no body, requires the `nexusprod_rt` cookie.

**Response 200**
```json
{
  "accessToken": "eyJhbGciOi...",
  "accessExpiresAt": "2026-06-24T10:45:00+00:00"
}
```

**Response 401** — missing/expired/revoked cookie.

### `POST /api/auth/logout`

Revokes the current access token's JTI (so it cannot be reused even if not yet expired) and revokes the refresh token's JTI. Clears the refresh cookie.

**Auth**: required.

**Response 200**
```json
{ "success": true }
```

### `GET /api/auth/me`

Returns the current user from the access-token claims.

**Auth**: required.

**Response 200**
```json
{ "userId": 1, "userName": "admin" }
```

---

## Lookups — `/api/sections`, `/api/trips`, `/api/server-info`, `/api/health`

Anonymous endpoints used by the Login page before the user authenticates.

### `GET /api/sections`

Returns the active section list along with the parent category ID (the original Express app gates both fields, the .NET port keeps them so the frontend can show the breadcrumb without an extra round trip).

**Response 200**
```json
{
  "categoryId": 1,
  "sections": [
    { "id": 1, "name": "Fresh Bakery" },
    { "id": 2, "name": "Beverages" }
  ]
}
```

### `GET /api/trips?section={sectionId}`

Returns the trips that have an active invoice for any item in the given section.

**Query parameters**
- `section` — section ID (integer), e.g. `?section=1`.

**Response 200**
```json
{
  "trips": [
    { "id": 1, "trip": "06:00 AM Trip" },
    { "id": 2, "trip": "09:00 AM Trip" }
  ]
}
```

### `GET /api/server-info`

Returns the running server's version, current time, uptime, LAN addresses it is bound to, and the listening port. Used by the "About / connection" panel and by the updater.

**Response 200**
```json
{
  "version": "1.0.0",
  "serverTime": "2026-06-24T09:00:00+00:00",
  "uptimeSeconds": 3600.5,
  "lanAddresses": ["192.168.1.20", "10.0.0.15"],
  "port": 5099
}
```

### `GET /api/health`

Liveness probe. Returns a static payload.

**Response 200**
```json
{
  "status": "ok",
  "version": "1.0.0",
  "serverTime": "2026-06-24T09:00:00+00:00",
  "uptimeSeconds": 0
}
```

---

## Orders — `/api/orders`

All routes in this group require `AuthenticatedUser` policy (valid JWT).

### `GET /api/orders/{sectionId}/{tripId}`

Loads the order list for the given section + trip, with per-branch distribution. Items come from `inv31065bs` (bill summary) joined to `inv31066` / `inv31066bsd` (detail rows), with `pur_sale_id` exposed on each distribution row so the client can address it on update/exclude.

**URL parameters**
- `sectionId` — section ID.
- `tripId` — trip ID.

**Response 200**
```json
{
  "orders": [
    {
      "id": 1,
      "stockMastId": 1001,
      "totalQty": 75,
      "name": "Artisan Sourdough",
      "unit": "Loaf",
      "isCompleted": false,
      "distribution": [
        {
          "purSaleId": 9001,
          "branch": "Main Warehouse",
          "brnchId": 1,
          "trip": 1,
          "qty": 45,
          "availableTrips": [
            { "id": 1, "trip": "06:00 AM Trip" },
            { "id": 2, "trip": "09:00 AM Trip" }
          ]
        }
      ]
    }
  ]
}
```

### `GET /api/orders/check-pending`

Returns whether any rows in `order_distribution` are still pending invoice generation.

**Response 200**
```json
{ "pendingExist": true }
```

### `POST /api/orders/generate-invoices`

Scans `order_distribution` for pending rows, groups them by `(branch_id, trip_id)`, materializes `sales_master` / `sales_details` (or `sales_transfer_master` / `sales_transfer_details` for `is_for_transfer = 1`), and flips `inv_gen = 1`. Runs in a single transaction; returns the count of invoices written.

**Request**
```json
{ "userId": 1, "brnchId": 1, "userCounterId": 1 }
```

**Response 200**
```json
{ "success": true, "message": "21 invoices generated", "invoiceCount": 21 }
```

### `POST /api/orders/update`

Updates quantities for a single item across one or more branches in the current trip. Marks the affected detail rows complete. Recalculates `sales_master.total_value`. `OriginalQty` is the qty the client shows as the baseline; the actual UPDATE writes the `Qty` (nullable — `null` means "leave unchanged").

**Request**
```json
{
  "itemId": 1,
  "trip": 1,
  "distribution": [
    { "purSaleId": 9001, "stockMastId": 1001, "originalQty": 45, "branch": "Main Warehouse", "qty": 50 }
  ]
}
```

**Response 200**
```json
{ "success": true, "message": "Updated 1 row(s)" }
```

### `POST /api/orders/exclude`

Excludes an item from the current trip's invoice and (optionally) rolls the quantity over to the next trip. Each `Entries` row carries its own `targetTrip` so the client can roll some branches to trip N and others to trip N+1 in a single call. If a row's `targetTrip` is `null` the quantity is dropped entirely.

**Request**
```json
{
  "sectionId": 1,
  "itemId": 1,
  "currentTrip": 1,
  "stockMastId": 1001,
  "brnchId": null,
  "entries": [
    { "purSaleId": 9001, "qty": 45, "targetTrip": 2 }
  ]
}
```

When `brnchId` is `null` the exclusion applies to every branch carrying `stockMastId` in the current trip; otherwise only that branch. Returns a free-form human message with row counts and skip reasons.

**Response 200**
```json
{ "success": true, "message": "1 updated, 0 skipped, 1 carried forward, 0 carry skipped" }
```

---

## Config — `/api/config`

Anonymous, used by the first-run wizard before authentication exists.

### `POST /api/config/save`

Writes the connection credentials to `db_config.json` next to the running executable. The file is the authoritative source on next startup; the live `IConnectionFactory` picks the new values without a restart.

**Request**
```json
{
  "host": "localhost",
  "port": 3306,
  "user": "root",
  "password": "admin@5555",
  "database": "prod_app",
  "useMockDb": false
}
```

**Response 200**
```json
{ "success": true, "message": "Configuration saved" }
```

### `POST /api/config/test`

Pings a target MySQL server with the given credentials without writing them to disk. Used by the wizard's "Test connection" button.

**Request**: same as `/save` (no `useMockDb`).
**Response 200**
```json
{ "success": true, "message": "Connected in 23ms" }
```
**Response 500** on failure
```json
{ "success": false, "message": "Unable to connect: connect ECONNREFUSED 127.0.0.1:3306" }
```

---

## Updater — `/api/updater`

Anonymous. The updater is a separate long-running background service that polls an external manifest URL.

### `GET /api/updater/status`

Returns the current phase (`Idle`, `Checking`, `Downloading`, `Ready`, `Error`, etc.) and the latest seen version.

**Response 200**
```json
{
  "phase": "Idle",
  "message": null,
  "latestVersion": "1.1.0",
  "lastChecked": "2026-06-24T08:00:00+00:00"
}
```

### `POST /api/updater/check`

Forces an immediate check. Returns `accepted: false` if a check is already in progress.

**Response 200**
```json
{ "accepted": true, "message": "Update check started" }
```

---

## Error shapes

**Domain failure (HTTP 400)**
```json
{ "message": "Item 1 not found in current trip", "code": "InvalidInput" }
```

**Unhandled exception (HTTP 500)**
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.6.1",
  "title": "Server error",
  "status": 500,
  "detail": "Object reference not set to an instance of an object."
}
```

---

## Versioning

The base path is `/api` with no version segment. Breaking changes to the response shape are avoided; additive fields are not. The current `version` field on `/health` and `/server-info` is the assembly informational version (`1.0.0`).
