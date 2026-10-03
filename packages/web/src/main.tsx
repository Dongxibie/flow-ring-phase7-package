import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { RouterProvider, createBrowserRouter, type RouteObject } from 'react-router-dom';
import { Layout } from './Layout';
import { ProfileManagerPage } from './pages/ProfileManager/ProfileManagerPage';
import { RingStudioPage } from './pages/RingStudio/RingStudioPage';
import { SettingsPage } from './pages/Settings/SettingsPage';
import { FlowCodePage } from './pages/FlowCode/FlowCodePage';

// v9 重构：单一 Router，消除 v5-v8 的双重 Router 不确定性
//
// v5-v8 历史：
// - v5 之前：Phase 5 早期架构，main.tsx 用 RouterProvider 包 App，App 内 <Routes> 定义子路由
// - v8：把 App.tsx 子 Route 改成相对路径 + main.tsx path: '*' catch-all
// - 问题：React Router v6 不支持嵌套 Router（在 RouterProvider 下又用 <Routes>）
//
// v9 修法：彻底消除双重 Router
// - main.tsx 路由表只有 path: '/' 顶层 Layout + children 子路由
// - Layout 在 src/Layout.tsx（新文件），NavLink + Outlet
// - 子路由全在 main.tsx 里（无 catch-all hack）
// - WebView2 Navigate 到 https://flowring.local/（不带 /index.html）→ React Router 拿到 '/'
//
// 预期：用户开 Studio → 看到 Layout（顶部 NavLink 四条 + 主体 ProfileManagerPage 占位内容）
const routes: RouteObject[] = [
  {
    path: '/',
    element: <Layout />,
    children: [
      { index: true, element: <ProfileManagerPage /> },
      { path: 'studio', element: <RingStudioPage /> },
      { path: 'profiles', element: <ProfileManagerPage /> },
      { path: 'flow-code', element: <FlowCodePage /> },
      { path: 'settings', element: <SettingsPage /> },
    ],
  },
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