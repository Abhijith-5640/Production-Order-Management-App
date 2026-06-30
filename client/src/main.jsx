import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.jsx'

async function initApp() {
    // Only fetch runtime config if we are in Production mode
    if (!import.meta.env.DEV) {
        try {
            // Fetches config.json from the wwwroot folder over the network
            const response = await fetch('/config.json');
            if (response.ok) {
                const config = await response.json();
                window.API_BASE_URL = config.API_BASE_URL;
                console.log("Runtime configuration loaded:", window.API_BASE_URL);
            }
        } catch (error) {
            console.error("Could not load runtime config.json, using fallback endpoints.", error);
        }
    }
  }

createRoot(document.getElementById('root')).render(
  <StrictMode>
    <App />
  </StrictMode>,
)

initApp();
