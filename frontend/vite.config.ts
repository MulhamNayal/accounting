import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  // Deployed, the app is an IIS Application under a sub-path, so every asset URL and the
  // BASE_URL that api/client.ts resolves against have to carry it. The deploy workflow sets
  // VITE_BASE; local development stays at the origin root.
  base: process.env.VITE_BASE ?? '/',
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      // Proxying rather than calling the API's origin directly keeps the app on relative
      // URLs, which is how it will run in production behind a single origin. It also means
      // the frontend never depends on CORS being configured.
      '/api': {
        target: 'http://localhost:5100',
        changeOrigin: true,
      },
    },
  },
})
