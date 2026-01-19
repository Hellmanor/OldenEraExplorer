import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'path'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  build: {
    // Increase chunk size warning limit to accommodate Three.js (573 KB)
    // Three.js is only loaded when the 3D viewer is accessed, so this is acceptable
    chunkSizeWarningLimit: 600,
    rollupOptions: {
      output: {
        manualChunks: {
          // Core React libraries
          'react-vendor': ['react', 'react-dom', 'react-router-dom'],
          // UI and utility libraries
          'ui-vendor': ['@tanstack/react-query', 'dompurify'],
          // 3D viewer libraries (Three.js)
          'three-vendor': ['three', 'lil-gui'],
          // SignalR
          'signalr-vendor': ['@microsoft/signalr'],
        },
      },
    },
  },
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:5176',
        changeOrigin: true,
      },
      '/ws': {
        target: 'http://localhost:5176',
        ws: true,
        changeOrigin: true,
        // SignalR specific configuration
        configure: (proxy) => {
          proxy.on('error', (err) => {
            console.log('proxy error', err);
          });
          proxy.on('proxyReq', (_proxyReq, req) => {
            console.log('Proxying SignalR request:', req.method, req.url);
          });
          proxy.on('proxyRes', (proxyRes, req) => {
            console.log('SignalR proxy response:', proxyRes.statusCode, req.url);
          });
        },
      },
    },
  },
})
