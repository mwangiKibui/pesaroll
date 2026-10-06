import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// In Docker the API is reachable as http://api:8080; outside Docker, localhost.
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': process.env.API_URL ?? 'http://localhost:8080',
    },
  },
})
