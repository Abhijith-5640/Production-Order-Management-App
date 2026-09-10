# NexusProd — API Documentation

This document describes the REST endpoints exposed by the **Nexus Prod** server (ASP.NET Core 8, minimal API).

## Conventions

- **Base URL**: `http://localhost:5099/api` during development
- **Auth**: Most endpoints require a JWT bearer token: `Authorization: Bearer <accessToken>`
- **Token Refresh**: Short-lived access token (15 min), silently refreshed via HttpOnly cookie
- **Errors**: Domain failures return HTTP 400 with `{ "message": "...", "code": "..." }`. Unhandled exceptions return HTTP 500.
- **Content Type**: All request bodies are `application/json`

---

## Auth — `/api/auth`

### `POST /api/auth/login`

Authenticates user and returns access token + refresh cookie.

**Request**
```json
{ "username": "admin", "password": "12345" }
```

**Response 200**
```json
{
  "accessToken": "eyJhbGciOi...",
  "accessExpiresAt": "2026-09-10T10:30:00+00:00",
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

---

### `POST /api/auth/refresh`

Reads refresh cookie, validates JWT, returns new access token.

**Request**: No body (requires `nexusprod_rt` cookie)

**Response 200**
```json
{
  "accessToken": "eyJhbGciOi...",
  "accessExpiresAt": "2026-09-10T10:45:00+00:00"
}
```

**Response 401** — missing/expired/revoked cookie

---

### `POST /api/auth/logout`

Revokes tokens and clears refresh cookie.

**Auth**: Required

**Response 200**
```json
{ "success": true }
```

---

### `GET /api/auth/me`

Returns current user info from access token.

**Auth**: Required

**Response 200**
```json
{ "userId": 1, "userName": "admin" }
```

---

## Lookups — `/api/sections`, `/api/trips`, `/api/server-info`, `/api/health`

Anonymous endpoints (no auth required).

---

### `GET /api/sections`

Returns active section list with parent category ID.

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

---

### `GET /api/trips?section={sectionId}`

Returns trips with active invoices for the given section.

**Query Parameters**
| Parameter | Type | Description |
|-----------|------|-------------|
| `section` | int | Section ID |

**Response 200**
```json
{
  "trips": [
    { "id": 1, "trip": "06:00 AM Trip" },
    { "id": 2, "trip": "09:00 AM Trip" }
  ]
}
```

---

### `GET /api/server-info`

Returns server version, time, uptime, LAN addresses, and port.

**Response 200**
```json
{
  "version": "1.0.6",
  "serverTime": "2026-09-10T09:00:00+00:00",
  "uptimeSeconds": 3600.5,
  "lanAddresses": ["192.168.1.20", "10.0.0.15"],
  "port": 5099
}
```

---

### `GET /api/health`

Liveness probe.

**Response 200**
```json
{
  "status": "ok",
  "version": "1.0.6",
  "serverTime": "2026-09-10T09:00:00+00:00",
  "uptimeSeconds": 0
}
```

---

## Orders — `/api/orders`

All routes in this group require JWT authentication.

---

### `GET /api/orders?section={sectionId}&trip={tripId}`

Loads order list for the given section + trip, with per-branch distribution.

**Query Parameters**
| Parameter | Type | Description |
|-----------|------|-------------|
| `section` | int | Section ID |
| `trip` | int | Trip ID |

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
      "unitDecml": 3,
      "isCompleted": false,
      "distribution": [
        {
          "purSaleId": 9001,
          "branch": "Main Warehouse",
          "brnchId": 1,
          "trip": 1,
          "qty": 45,
          "originalQty": 45,
          "billNoStr": "INV-001",
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

**Distribution Fields**
| Field | Type | Description |
|-------|------|-------------|
| `purSaleId` | int | Unique identifier for this distribution row |
| `branch` | string | Branch/store name |
| `brnchId` | int | Branch ID |
| `trip` | int | Current trip ID |
| `qty` | decimal | Current quantity |
| `originalQty` | decimal | Original quantity before edits |
| `billNoStr` | string | Invoice reference |
| `availableTrips` | array | Trips available for routing |

---

### `GET /api/orders/check-pending?brnchId={branchId}`

Checks if any rows are pending invoice generation.

**Query Parameters**
| Parameter | Type | Description |
|-----------|------|-------------|
| `brnchId` | int? | Optional branch filter |

**Response 200**
```json
{ "pendingExist": true }
```

---

### `GET /api/orders/tariff-violations?brnchId={branchId}`

Returns items that violate purchasing tariff rules.

**Query Parameters**
| Parameter | Type | Description |
|-----------|------|-------------|
| `brnchId` | int? | Optional branch filter |

**Response 200**
```json
{
  "hasViolations": true,
  "totalItems": 3,
  "totalBranches": 2,
  "branches": [
    {
      "branchName": "Main Warehouse",
      "branchId": 1,
      "items": [
        { "itemId": 101, "itemCode": "ITM001", "itemName": "Item Name", "unit": "Pcs", "qty": 50 }
      ]
    }
  ]
}
```

---

### `POST /api/orders/generate`

Generates invoices from pending order distribution rows.

**Request**
```json
{
  "userId": 1,
  "brnchId": 1,
  "userCounterId": 1
}
```

**Response 200**
```json
{
  "success": true,
  "message": "21 invoices generated",
  "invoiceCount": 21
}
```

---

### `POST /api/orders/update`

Updates quantities for a single item across branches. Marks rows complete.

**Request**
```json
{
  "itemId": 1,
  "trip": 1,
  "distribution": [
    {
      "purSaleId": 9001,
      "stockMastId": 1001,
      "originalQty": 45,
      "branch": "Main Warehouse",
      "qty": 50
    }
  ],
  "usrId": 1
}
```

**Distribution Fields**
| Field | Type | Description |
|-------|------|-------------|
| `purSaleId` | int | Distribution row ID |
| `stockMastId` | int | Item ID |
| `originalQty` | decimal | Baseline quantity |
| `branch` | string | Branch name |
| `qty` | decimal? | New quantity (null = unchanged) |

**Response 200**
```json
{ "success": true, "message": "Updated 1 row(s)" }
```

---

### `POST /api/orders/exclude`

Excludes item from current trip and optionally routes to next trip.

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
  ],
  "usrId": 1
}
```

**Request Fields**
| Field | Type | Description |
|-------|------|-------------|
| `sectionId` | int | Section ID |
| `itemId` | int | Item ID |
| `currentTrip` | int | Current trip ID |
| `stockMastId` | int | Stock master ID |
| `brnchId` | int? | Specific branch (null = all branches) |
| `entries` | array | Per-row exclusion entries |
| `usrId` | int | User ID |

**Entry Fields**
| Field | Type | Description |
|-------|------|-------------|
| `purSaleId` | int | Distribution row ID |
| `qty` | decimal | Quantity to exclude |
| `targetTrip` | int? | Route to trip (null = discard) |

**Response 200**
```json
{ "success": true, "message": "1 updated, 0 skipped, 1 carried forward, 0 carry skipped" }
```

---

## Config — `/api/config`

Anonymous endpoints for database configuration wizard.

---

### `POST /api/config/save`

Saves database credentials to `db_config.json`.

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

---

### `POST /api/config/test`

Tests database connection without saving.

**Request**: Same as `/save` (without `useMockDb`)

**Response 200**
```json
{ "success": true, "message": "Connected in 23ms" }
```

**Response 500** — connection failed
```json
{ "success": false, "message": "Unable to connect: connect ECONNREFUSED 127.0.0.1:3306" }
```

---

## Updater — `/api/updater`

Anonymous. Background service for auto-updates.

---

### `GET /api/updater/status`

Returns current update phase and latest version.

**Response 200**
```json
{
  "phase": "Idle",
  "message": null,
  "latestVersion": "1.0.6",
  "lastChecked": "2026-09-10T08:00:00+00:00"
}
```

---

### `POST /api/updater/check`

Forces immediate update check.

**Response 200**
```json
{ "accepted": true, "message": "Update check started" }
```

---

## Error Shapes

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

## Version

Current version: **1.0.6** (from `package.json` assembly version)

The `/health` and `/server-info` endpoints return the current version.
