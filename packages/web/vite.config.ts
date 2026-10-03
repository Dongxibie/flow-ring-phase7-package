import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// RingUI 出站为 packages/web/dist/，由 DesktopHost 的 WebView2 加载。
// 开发模式下 dev server 监听 5179，host 通过 webView2.CoreWebView2.Environment
// 在 Phase 3 起指向 http://localhost:5179；MVP 仅打 dist 包内嵌。
//
// v6 修复：Vite build 显式 base='./'，让生成的 index.html 用相对路径引用 assets/...
// WebView2 用 file:// 加载 dist/index.html，base='/' 默认值会导致 /assets/... 解析为
// file:///assets/...（磁盘根），所有静态资源 404，主页面看起来白屏。
export default defineConfig({
  base: './',
  plugins: [react()],
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    target: 'es2022',
    sourcemap: true,
    rollupOptions: {
      output: {
        manualChunks: {
          react: ['react', 'react-dom', 'react-router-dom'],
        },
      },
    },
  },
  server: {
    port: 5179,
    strictPort: true,
  },
});