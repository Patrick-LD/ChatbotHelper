import { fileURLToPath, URL } from 'node:url'

import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import vueDevTools from 'vite-plugin-vue-devtools'

// https://vite.dev/config/
export default defineConfig({
  plugins: [vue(), vueDevTools()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    port: 5173,
    // I udvikling går kald til /api gennem Vite til API'et på :5022. Så er browserens origin den samme,
    // og API'et behøver ingen CORS lokalt. I produktion peger VITE_API_BASE direkte på API'et (CORS).
    proxy: {
      '/api': {
        target: 'http://localhost:5022',
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/api/, ''),
      },
    },
  },
})
