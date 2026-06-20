import React, { useEffect } from 'react';
import { BrowserRouter, Routes, Route, Navigate, useNavigate } from 'react-router-dom';
import { ToastContainer } from 'react-toastify';
import 'react-toastify/dist/ReactToastify.css';

import Login from './pages/Login';
import Dashboard from './pages/Dashboard';
import { authStore, refreshAccessToken, dispatchSessionExpired, SESSION_EXPIRED_EVENT } from './services/api';

// ---------------------------------------------------------------------------
// SessionExpiredBridge
//
// Single owner of the `nexus:session_expired` event. Lives at the top of the
// router tree so it is mounted for the entire life of the app — meaning the
// handler exists regardless of whether the user is currently on /login or /.
//
// This closes the "listener dead zone" bug: previously the listener was
// attached by Login.jsx, but Login.jsx only mounts on /login. If the cold-
// start refresh failed while the user was still on /, the event fired into
// the void and the user got stranded on the dashboard with a broken
// session.
//
// Re-entrancy: a guard on `window.__nexusLastSessionExpired` debounces
// duplicate dispatches that arrive in the same tick (e.g. a `request()` in
// the Dashboard firing `nexus:session_expired` while the cold-start refresh
// in PrivateRoute is also failing).
// ---------------------------------------------------------------------------
const SessionExpiredBridge = () => {
  const navigate = useNavigate();

  useEffect(() => {
    const onSessionExpired = () => {
      // dispatchSessionExpired() in api.js already debounces duplicate
      // events within a 1s window, so by the time we get here we know
      // this is the only dispatch that matters. Clear the session and
      // route to /login. Using navigate (not window.location) keeps us
      // inside the React Router — works identically in dev (Vite) and
      // prod (served from wwwroot on :5099).
      authStore.clearSession();
      navigate('/login', { replace: true });
    };

    window.addEventListener(SESSION_EXPIRED_EVENT, onSessionExpired);
    return () => window.removeEventListener(SESSION_EXPIRED_EVENT, onSessionExpired);
  }, [navigate]);

  return null;
};

// Protected Route Component.
// Gate on the `nexus_authenticated` flag (set by authStore.setSession) so a
// session with a stale access token but a valid refresh cookie gets a chance
// to refresh instead of being bounced to /login on first paint. If the
// refresh itself fails, we dispatch `SESSION_EXPIRED_EVENT` directly so the
// top-level bridge handles the redirect — no need to depend on a downstream
// request() call to surface the failure.
const PrivateRoute = ({ children }) => {
  if (!authStore.isAuthenticated()) {
    authStore.clearSession();
    return <Navigate to="/login" replace />;
  }

  const expires = authStore.getExpiresAt();
  const isExpired = expires && Date.now() > expires;

  // Best-effort silent refresh on cold start. On failure, dispatch the
  // session-expired event ourselves; SessionExpiredBridge will redirect.
  // Route through dispatchSessionExpired() so the re-entrancy guard in
  // api.js debounces against any concurrent failure from Dashboard's
  // first fetch.
  if (isExpired) {
    refreshAccessToken().catch(() => {
      dispatchSessionExpired();
    });
  }
  return children;
};

function App() {
  return (
    <BrowserRouter>
      <SessionExpiredBridge />
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
