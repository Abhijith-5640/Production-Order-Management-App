import React, { useEffect, useState } from 'react';
import { BrowserRouter, Routes, Route, Navigate, useNavigate } from 'react-router-dom';
import { ToastContainer } from 'react-toastify';
import 'react-toastify/dist/ReactToastify.css';

import Login from './pages/Login';
import Dashboard from './pages/Dashboard';
import FullScreenLoader from './components/FullScreenLoader';
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
// to refresh instead of being bounced to /login on first paint.
//
// CRITICAL: if the access token is expired, we MUST wait for the refresh
// promise to resolve before rendering children. Otherwise Dashboard's first
// fetch fires while the refresh is still in flight, gets a 401, and fires
// SESSION_EXPIRED_EVENT — bouncing the user to /login even though the
// refresh would have succeeded. This was the "dashboard flashes, then
// redirects to login" bug.
const PrivateRoute = ({ children }) => {
  if (!authStore.isAuthenticated()) {
    authStore.clearSession();
    return <Navigate to="/login" replace />;
  }

  const expires = authStore.getExpiresAt();
  // Treat a missing expiry (0) as expired — this covers tokens issued before
  // the expiry field was added to storage, or corrupted entries. The cold-start
  // refresh will verify the token's real validity with the server.
  const isExpired = !expires || Date.now() > expires;

  // Token still valid — render immediately.
  if (!isExpired) {
    return children;
  }

  // Token expired — wait for the cold-start refresh to settle before
  // mounting children. Use a wrapper component (not a sync branch) so
  // the refresh promise is started in useEffect and the result drives a
  // state-driven re-render.
  return <ColdStartRefresh>{children}</ColdStartRefresh>;
};

// Helper component for PrivateRoute: shows a loader while the cold-start
// /auth/refresh call is in flight, then renders children on success or
// navigates to /login on failure. By the time children mount, either the
// token is fresh or the session is fully cleared — no race against the
// first Dashboard fetch.
const ColdStartRefresh = ({ children }) => {
  const [status, setStatus] = useState('refreshing'); // 'refreshing' | 'ready'

  useEffect(() => {
    let cancelled = false;
    refreshAccessToken()
      .then(() => {
        if (!cancelled) setStatus('ready');
      })
      .catch(() => {
        if (!cancelled) {
          // Route through dispatchSessionExpired() so the re-entrancy
          // guard in api.js debounces against any concurrent failure.
          dispatchSessionExpired();
        }
      });
    return () => {
      cancelled = true;
    };
  }, []);

  if (status === 'ready') {
    return children;
  }
  return <FullScreenLoader isVisible={true} text="Restoring session..." />;
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
