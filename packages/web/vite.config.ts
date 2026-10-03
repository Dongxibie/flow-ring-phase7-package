import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// RingUI 出站为 packages/web/dist/，由 DesktopHost 的 WebView2 加载。
// 开发模式下 dev server 监听 5179，host 通过 webView2.CoreWebView2.Environment
// 在 Phase 3 起指向 http://localhost:5179；MVP 仅打 dist 包内嵌。
//
// v18.6 修法：vite build 改 base 为空字符串（不输出 ./ 前缀）
//
// v6 改 base='./' 当时修的是 file:// 协议路径——但 WebView2 用 SetVirtualHostNameToFolderMapping
// 后 `./` 相对路径仍可能 404：HTML 在 https://flowring.local/index.html 加载，
// `<script src="./assets/...">` WebView2 解析为 `https://flowring.local/./assets/...`（含点）
// 或只取父目录导致资源 404 → JS bundle 不跑 → React 没 mount → 主体白屏。
//
// v18.5 实测：dist/index.html 输出 `./assets/index-Xchg7aiY.js`，host 跑后收不到任何
// WebMessageReceived（前端没 mount + postMessage 没发），确认根因是资源加载失败。
//
// v18.6 改 base 为绝对路径 '/':
// - HTML 输出 <script src="/assets/index-Xchg7aiY.js">
// - 在 https://flowring.local/index.html 加载 → 绝对路径自动解析为
//   https://flowring.local/assets/index-Xchg7aiY.js
// - JS bundle 加载成功 → React mount → postState 触发 → host WebMessageReceived 看到 'app.mount'
//
// 为什么不用 base='' (空字符串)：
// - vite v6.4.3 实测 base='' 仍输出 './assets/...' 相对路径（不是裸路径）
// - WebView2 SetVirtualHostNameToFolderMapping 后 `./` 相对路径可能 404
// - base='/' 输出绝对路径，HTML 加载时自动按 origin 解析为 https://flowring.local/assets/...
export default defineConfig({
  base: '/',
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