import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// https://vite.dev/config/
export default defineConfig({
  plugins: [vue()],
  build: {
    chunkSizeWarningLimit: 1400,
    rolldownOptions: {
      output: {
        manualChunks(id) {
          if (!id.includes('node_modules')) return undefined
          if (id.includes('ant-design-vue') || id.includes('@ant-design')) return 'vendor-antd'
          if (id.includes('vue') || id.includes('pinia') || id.includes('vue-router')) return 'vendor-vue'
          if (id.includes('qrcode')) return 'vendor-qrcode'
          if (id.includes('axios')) return 'vendor-http'
          return 'vendor'
        },
      },
    },
  },
})
