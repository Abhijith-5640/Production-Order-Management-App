import React from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { ToastContainer } from 'react-toastify';
import 'react-toastify/dist/ReactToastify.css';

import Login from './pages/Login';
import Dashboard from './pages/Dashboard';
import { authStore, refreshAccessToken } from './services/api';

// Protected Route Component.
// Gate on the `nexus_authenticated` flag (set by authStore.setSession) so a
// session with a stale access token but a valid refresh cookie gets a chance
// to refresh instead of being bounced to /login on first paint. If the
// refresh itself fails, services/api.js dispatches `nexus:session_expired`,
// which the Login page's listener catches and routes back to /login.
const PrivateRoute = ({ children }) => {
  if (!authStore.isAuthenticated()) {
    authStore.clearSession();
    return <Navigate to="/login" replace />;
  }

  const expires = authStore.getExpiresAt();
  const isExpired = expires && Date.now() > expires;

  // Best-effort silent refresh on cold start. If it fails the user is
  // redirected via the session_expired event; if it succeeds the dashboard
  // mounts and its first fetch uses the fresh token (or one is already
  // queued via refreshInFlight in api.js).
  if (isExpired) {
    refreshAccessToken().catch(() => { /* handled by the listener */ });
  }
  return children;
};

function App() {
  return (
    <BrowserRouter>
      <div className="font-sans bg-slate-100 min-h-screen">
        <Routes>
          <Route path="/login" element={<Login />} />
          <Route
            path="/"
            element={
              <PrivateRoute>
                <Dashboard />
              </PrivateRoute>
            }
          />
        </Routes>
        <ToastContainer position="bottom-center" />
      </div>
    </BrowserRouter>
  );
}

export default App;
