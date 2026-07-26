import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
export default defineConfig({
  plugins: [react()],
  build: { chunkSizeWarningLimit: 700 },
  server: { proxy: { '/api': 'http://localhost:5102', '/health': 'http://localhost:5102' } },
  test: { environment: 'jsdom', include: ['src/**/*.test.{ts,tsx}'], setupFiles: ['./src/test/setup.ts'] }
})
