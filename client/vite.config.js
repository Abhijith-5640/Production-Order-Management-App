import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

// The API default points at the new .NET server. Override with VITE_API_TARGET.
const DEFAULT_API_TARGET = 'http://127.0.0.1:8443';

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '');
  const apiTarget = env.VITE_API_TARGET || DEFAULT_API_TARGET;

  return {
    plugins: [react(), tailwindcss()],
    build: {
      outDir: '../src/NexusProd.Api/wwwroot',
      emptyOutDir: true,
      sourcemap: true,
      rollupOptions: {
        output: {
          manualChunks: {
            'react-vendor': ['react', 'react-dom', 'react-router-dom'],
          },
        },
      },
    },
    server: {
      port: 5173,
      strictPort: false,
      proxy: {
        '/api': {
          target: apiTarget,
          changeOrigin: true,
          // ws: false, // no need for HMR-over-proxy for our case
        },
      },
    },
  };
});
