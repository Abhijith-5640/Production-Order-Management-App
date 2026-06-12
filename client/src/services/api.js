// c:/Users/devda/source/repos/anti-gra/Production-Order-Management-App/client/src/services/api.js
//
// Routes here mirror the .NET endpoints declared in src/NexusProd.Api/Api/Endpoints/*.
// Keep them in sync with the server.
//
// Dev base URL points at the .NET server directly on :5099 (Vite's /api proxy is also
// wired up in vite.config.js for HMR convenience, but the explicit base works in both
// dev and prod without relying on the proxy).
const DEV_API_BASE_URL = 'http://localhost:5099/api';
const API_BASE_URL = import.meta.env.DEV ? DEV_API_BASE_URL : '/api';

// Read the JWT once and re-attach it to protected calls. The server's /api/orders
// group requires the "AuthenticatedUser" policy.
const authHeaders = () => {
    const token = localStorage.getItem('nexus_token');
    return token ? { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' } : { 'Content-Type': 'application/json' };
};

const request = async (path, init = {}) => {
    const response = await fetch(`${API_BASE_URL}${path}`, {
        ...init,
        headers: { ...authHeaders(), ...(init.headers || {}) },
    });
    // Throw on non-2xx so the caller's catch can surface the real status / message.
    if (!response.ok) {
        let detail = '';
        try { detail = (await response.json())?.message || (await response.text()); } catch { /* ignore */ }
        throw new Error(`HTTP ${response.status} ${response.statusText}${detail ? ` - ${detail}` : ''}`);
    }
    return response.json();
};

export const api = {
    login: async (username, password) => request('/auth/login', {
        method: 'POST',
        body: JSON.stringify({ username, password }),
    }),

    logout: async () => request('/auth/logout', { method: 'POST' }),

    getSections: async () => request('/sections'),

    getTrips: async (sectionId) => request(`/trips?section=${encodeURIComponent(sectionId)}`),

    getOrders: async (sectionId, trip) =>
        request(`/orders?section=${encodeURIComponent(sectionId)}&trip=${encodeURIComponent(trip)}`),

    updateInvoice: async (itemId, trip, newDistribution) => request('/orders/update', {
        method: 'POST',
        body: JSON.stringify({ itemId, trip, newDistribution }),
    }),

    saveConfig: async (configData) => request('/config/save', {
        method: 'POST',
        body: JSON.stringify(configData),
    }),

    testDb: async (configData) => request('/config/test', {
        method: 'POST',
        body: JSON.stringify(configData),
    }),

    excludeItem: async (sectionId, itemId, currentTrip, stockMastId, brnchId = null, purSaleIds = []) => request('/orders/exclude', {
        method: 'POST',
        body: JSON.stringify({ sectionId, itemId, currentTrip, stockMastId, brnchId, purSaleIds }),
    }),

    checkPendingOrders: async (BrnchId) => request(`/orders/check-pending?brnchId=${encodeURIComponent(BrnchId)}`),

    generateInvoices: async (userId) => request('/orders/generate', {
        method: 'POST',
        body: JSON.stringify({ userId }),
    }),
};
