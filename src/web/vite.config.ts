import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  build: {
    // Never inline assets as data: URIs; the Content-Security-Policy only allows
    // fonts from the same origin (font-src 'self').
    assetsInlineLimit: 0,
  },
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:8080',
      '/health': 'http://localhost:8080',
    },
  },
})

