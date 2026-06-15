import React from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { ToastContainer } from 'react-toastify';
import 'react-toastify/dist/ReactToastify.css';

import Login from './pages/Login';
import Dashboard from './pages/Dashboard';

// Protected Route Component
const PrivateRoute = ({ children }) => {
  const authed = localStorage.getItem('nexus_authenticated') === 'true';
  const token = localStorage.getItem('nexus_token');
  const expires = localStorage.getItem('nexus_token_expires');
  const isExpired = expires && Date.now() > new Date(expires).getTime();

  if (authed && token && !isExpired) return children;

  // Wipe any partial/stale auth state so the next session starts clean.
  localStorage.removeItem('nexus_authenticated');
  localStorage.removeItem('nexus_token');
  localStorage.removeItem('nexus_token_expires');
  localStorage.removeItem('nexus_user');
  localStorage.removeItem('nexus_user_id');
  localStorage.removeItem('nexus_user_brnch_id');
  return <Navigate to="/login" replace />;
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
