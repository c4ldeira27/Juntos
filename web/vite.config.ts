import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// In dev, Vite (5173) proxies the back-end routes to ASP.NET (5080).
// To the browser everything comes from the same origin, just like production (where .NET serves the React build).
// IMPORTANT: changeOrigin: false keeps the Host "localhost:5173", so ASP.NET builds Google's redirect_uri
// as http://localhost:5173/signin-google. The shorthand ('/api': 'http://...') turns changeOrigin on,
// the Host becomes "localhost:5080" and Google rejects it with redirect_uri_mismatch.
const backend = { target: 'http://localhost:5080', changeOrigin: false }

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': backend,
      '/auth': backend,
      '/signin-google': backend,
      '/health': backend,
    },
  },
})
