import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// Em dev o Vite (5173) repassa as rotas do backend para o ASP.NET (5080).
// Para o navegador tudo vem da mesma origem, igual em produção (onde o .NET serve o build do React).
// IMPORTANTE: changeOrigin: false mantém o Host "localhost:5173", e o ASP.NET monta o redirect_uri do Google
// como http://localhost:5173/signin-google. A forma curta ('/api': 'http://...') liga changeOrigin: true,
// o Host vira "localhost:5080" e o Google recusa com redirect_uri_mismatch.
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
