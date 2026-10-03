import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { RouterProvider, createBrowserRouter, type RouteObject } from 'react-router-dom';
import { App } from './App';

// v8 修复：路由表用 catch-all 让任何路径都能渲染 App
//
// 根因（已确认）：WebView2 通过 SetVirtualHostNameToFolderMapping 把 dist 目录映射成
// https://flowring.local，Navigate 到 https://flowring.local/index.html。
// React Router createBrowserRouter 拿到的当前路径是 '/index.html'，不匹配 main.tsx 路由表
// 里的 path: '/'，抛 ErrorResponse(404) ErrorBoundary 显示 "Unexpected Application Error! 404 Not Found"。
//
// App.tsx 内部又用 <Routes> 定义了一组绝对路径（/studio /profiles /flow-code /settings），
// 但 React Router v6 不允许嵌套 Router（App 内 <Routes> 在 <RouterProvider> 下是独立子 Router）
// 且子 Route 的 path 必须是相对路径（不能以 / 开头）。
//
// v8 修法（最小改动）：
//   1. main.tsx 路由表 path 改成 '*' catch-all，App 作为 element
//   2. App.tsx 内部 Routes 保持，但绝对路径改成相对路径（去掉前缀）
//   3. App.tsx 内 Layout 用 <Outlet/> 渲染子路由
//
// 这样：
//   - 任何路径（包括 /index.html、/studio、/profiles 等）都进 App
//   - App 内部 Routes 接管路径匹配
//   - NavLink to="/studio" 仍能正确跳转
//
// 完整重构到 main.tsx 单 Router（v1.1+ 待办）：
//   - main.tsx 直接持有 Layout + 四页路由
//   - App.tsx 退化为 Layout 组件（NavLink + Outlet）
//   - 没有双重 Router
const routes: RouteObject[] = [
  { path: '*', element: <App /> },
];

const router = createBrowserRouter(routes);

const container = document.getElementById('root');
if (container === null) {
  throw new Error('未找到 #root 容器；index.html 必须包含 <div id="root"></div>。');
}

createRoot(container).render(
  <StrictMode>
    <RouterProvider router={router} />
  </StrictMode>,
);