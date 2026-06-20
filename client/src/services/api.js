// c:/Users/devda/source/repos/anti-gra/Production-Order-Management-App/client/src/services/api.js
//
// Routes here mirror the .NET endpoints declared in src/NexusProd.Api/Api/Endpoints/*.
// Keep them in sync with the server.
//
// Dev base URL points at the .NET server directly on :5099 (Vite's /api proxy is also
// wired up in vite.config.js for HMR convenience, but the explicit base works in both
// dev and prod without relying on the proxy).
//
// Auth flow:
//  - The access token is stored in localStorage and sent as `Authorization: Bearer …`.
//  - The refresh token lives in an HttpOnly cookie set by the API; it travels with
//    `credentials: 'include'` on every request.
//  - When an API call returns 401 with body `{ error: "token_expired" }`, we silently
//    call POST /api/auth/refresh once, store the new access token, and retry the
//    original request. Concurrent 401s share a single in-flight refresh.
//  - If refresh itself fails, or the server returns 401 with body `Token revoked`,
//    we clear local auth and dispatch `nexus:session_expired` so the app can route
//    the user back to /login.
const DEV_API_BASE_URL = 'http://localhost:5099/api';
const API_BASE_URL = import.meta.env.DEV ? DEV_API_BASE_URL : '/api';

const TOKEN_KEY     = 'nexus_token';
const EXPIRES_KEY   = 'nexus_token_expires';
const AUTH_FLAG_KEY = 'nexus_authenticated';
const USER_INFO_KEYS = [
    'nexus_user', 'nexus_user_id', 'nexus_user_brnch_id', 'nexus_user_counter_id',
];

// Single source of truth for the session-expired event name. Imported by
// App.jsx (top-level listener) and Login.jsx (toast).
export const SESSION_EXPIRED_EVENT = 'nexus:session_expired';

// Dispatch the session-expired event with a small re-entrancy guard.
// Multiple in-flight requests can all reject with SESSION_EXPIRED in the
// same tick (e.g. one from PrivateRoute's cold-start refresh, one from the
// Dashboard's first fetch). We dedupe dispatches within a short window so
// the listener — which triggers a router navigation — only runs once.
//
// Exported so PrivateRoute's cold-start refresh can route through the same
// guard instead of bypassing it with a raw dispatchEvent.
export function dispatchSessionExpired() {
    const last = window.__nexusLastSessionExpired || 0;
    if (Date.now() - last < 1000) return;
    window.__nexusLastSessionExpired = Date.now();
    window.dispatchEvent(new CustomEvent(SESSION_EXPIRED_EVENT));
}

// ---------------------------------------------------------------------------
// Auth store — single source of truth for the localStorage session keys.
// ---------------------------------------------------------------------------
export const authStore = {
    getToken: () => localStorage.getItem(TOKEN_KEY),

    getExpiresAt: () => {
        const v = localStorage.getItem(EXPIRES_KEY);
        return v ? new Date(v).getTime() : 0;
    },

    setSession: ({ accessToken, accessExpiresAt }) => {
        if (accessToken) localStorage.setItem(TOKEN_KEY, accessToken);
        if (accessExpiresAt) localStorage.setItem(EXPIRES_KEY, accessExpiresAt);
        localStorage.setItem(AUTH_FLAG_KEY, 'true');
    },

    clearSession: () => {
        [TOKEN_KEY, EXPIRES_KEY, AUTH_FLAG_KEY, ...USER_INFO_KEYS]
            .forEach((k) => localStorage.removeItem(k));
    },

    isAuthenticated: () => localStorage.getItem(AUTH_FLAG_KEY) === 'true',
};

// ---------------------------------------------------------------------------
// Singleton refresh promise. While a refresh is in flight, every concurrent
// caller awaits the SAME promise — so 10 parallel 401s trigger 1 refresh.
// Exported so PrivateRoute can do a best-effort refresh on cold start.
// ---------------------------------------------------------------------------
export let refreshInFlight = null;

export async function refreshAccessToken() {
    // No Authorization header on this call — we are refreshing, not requesting.
    const res = await fetch(`${API_BASE_URL}/auth/refresh`, {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
    });

    let body = null;
    try { body = await res.clone().json(); } catch { /* body may be empty */ }

    if (!res.ok || !body?.accessToken) {
        const err = new Error(body?.message || `Refresh failed (${res.status})`);
        err.code = 'REFRESH_FAILED';
        err.status = res.status;
        throw err;
    }

    authStore.setSession({
        accessToken: body.accessToken,
        accessExpiresAt: body.accessExpiresAt,
    });
    return body.accessToken;
}

function getRefreshPromise() {
    if (!refreshInFlight) {
        refreshInFlight = refreshAccessToken().finally(() => {
            refreshInFlight = null;
        });
    }
    return refreshInFlight;
}

// ---------------------------------------------------------------------------
// request(): single call site that does the silent-refresh dance.
// ---------------------------------------------------------------------------
async function rawFetch(path, init) {
    const headers = { 'Content-Type': 'application/json', ...(init.headers || {}) };
    const token = authStore.getToken();
    if (token) headers.Authorization = `Bearer ${token}`;
    return fetch(`${API_BASE_URL}${path}`, {
        ...init,
        headers,
        credentials: 'include',
    });
}

async function request(path, init = {}, _isRetry = false) {
    const response = await rawFetch(path, init);

    if (response.ok) return response.json();

    let body = null;
    try { body = await response.clone().json(); } catch { /* not JSON */ }

    const isTokenExpired =
        response.status === 401 &&
        body &&
        body.error === 'token_expired';

    if (isTokenExpired && !_isRetry) {
        try {
            await getRefreshPromise();
            return await request(path, init, true);
        } catch {
            authStore.clearSession();
            dispatchSessionExpired();
            const err = new Error('Session expired. Please log in again.');
            err.code = 'SESSION_EXPIRED';
            throw err;
        }
    }

    // Blacklisted access token (server's JwtBlacklistMiddleware returns this
    // shape for revoked JTIs). Treat it as a hard logout — the user can't
    // recover by refreshing, so route them back to /login.
    if (response.status === 401 && body?.message === 'Token revoked') {
        authStore.clearSession();
        dispatchSessionExpired();
    }

    // Everything else (real auth failure, server error, etc.) surfaces as before.
    const detail = body?.message || body?.error || '';
    const e = new Error(
        `HTTP ${response.status} ${response.statusText}${detail ? ' - ' + detail : ''}`,
    );
    e.status = response.status;
    e.body = body;
    throw e;
}

export const api = {
    login: (username, password) =>
        request('/auth/login', {
            method: 'POST',
            body: JSON.stringify({ username, password }),
        }, false),

    logout: () => request('/auth/logout', { method: 'POST' }, false),

    getSections: () => request('/sections'),

    getTrips: (sectionId) => request(`/trips?section=${encodeURIComponent(sectionId)}`),

    getOrders: (sectionId, trip) =>
        request(`/orders?section=${encodeURIComponent(sectionId)}&trip=${encodeURIComponent(trip)}`),

    updateInvoice: (itemId, trip, newDistribution) => request('/orders/update', {
        method: 'POST',
        body: JSON.stringify({ itemId, trip, newDistribution }),
    }),

    saveConfig: (configData) => request('/config/save', {
        method: 'POST',
        body: JSON.stringify(configData),
    }),

    testDb: (configData) => request('/config/test', {
        method: 'POST',
        body: JSON.stringify(configData),
    }),

    excludeItem: (sectionId, itemId, currentTrip, stockMastId, brnchId = null, purSaleIds = []) => request('/orders/exclude', {
        method: 'POST',
        body: JSON.stringify({ sectionId, itemId, currentTrip, stockMastId, brnchId, purSaleIds }),
    }),

    checkPendingOrders: (BrnchId) =>
        request(`/orders/check-pending?brnchId=${encodeURIComponent(BrnchId)}`),

    generateInvoices: (userId, brnchId, userCounterId) => request('/orders/generate', {
        method: 'POST',
        body: JSON.stringify({ userId, brnchId, userCounterId }),
    }),
};

export const isSessionExpired = (e) => e?.code === 'SESSION_EXPIRED';
