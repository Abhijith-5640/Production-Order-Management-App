// c:/Users/devda/source/repos/anti-gra/Production-Order-Management-App/client/src/services/api.js
const API_BASE_URL = import.meta.env.DEV 
    ? 'http://localhost:5000/api' 
    : '/api';

export const api = {
    login: async (username, password) => {
        const response = await fetch(`${API_BASE_URL}/login`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ username, password })
        });
        return response.json();
    },

    getSections: async () => {
        const response = await fetch(`${API_BASE_URL}/sections`);
        return response.json();
    },

    getTrips: async (section) => {
        const response = await fetch(`${API_BASE_URL}/trips/${encodeURIComponent(section)}`);
        return response.json();
    },

    getOrders: async (section, trip) => {
        const response = await fetch(`${API_BASE_URL}/orders/${encodeURIComponent(section)}/${encodeURIComponent(trip)}`);
        return response.json();
    },

    updateInvoice: async (itemId, trip, newDistribution) => {
        const response = await fetch(`${API_BASE_URL}/orders/update`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ itemId, trip, newDistribution })
        });
        return response.json();
    },

    saveConfig: async (configData) => {
        const response = await fetch(`${API_BASE_URL}/config`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(configData)
        });
        return response.json();
    },

    testDb: async (configData) => {
        const response = await fetch(`${API_BASE_URL}/test-db`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(configData)
        });
        return response.json();
    },

    excludeItem: async (section, itemId, currentTrip, branch = null) => {
        const response = await fetch(`${API_BASE_URL}/orders/exclude`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ section, itemId, currentTrip, branch })
        });
        return response.json();
    },

    checkPendingOrders: async () => {
        const response = await fetch(`${API_BASE_URL}/orders/check-pending`);
        return response.json();
    },

    generateInvoices: async (userId) => {
        const response = await fetch(`${API_BASE_URL}/orders/generate-invoices`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ userId })
        });
        return response.json();
    },

    adjustInvoice: async (itemId, currentTrip, section, updates) => {
        const response = await fetch(`${API_BASE_URL}/orders/adjust`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ itemId, currentTrip, section, updates })
        });
        return response.json();
    }
}
