import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { RouterProvider, createHashRouter, type RouteObject } from 'react-router-dom';
import { Layout } from './Layout';
import { ProfileManagerPage } from './pages/ProfileManager/ProfileManagerPage';
import { RingStudioPage } from './pages/RingStudio/RingStudioPage';
import { SettingsPage } from './pages/Settings/SettingsPage';
import { FlowCodePage } from './pages/FlowCode/FlowCodePage';

// v14 修法：用 createHashRouter 替代 createBrowserRouter
//
// v11 / v12 / v13 实测白屏根因：
// - v10 catch-all 顶层 path '*' + children index：/index.html 进 Layout 但 children index 不匹配 → Outlet 空
// - v11 children catch-all（嵌套）：React Router v6 嵌套 catch-all 行为异常，Layout 不渲染
// - v12 path '/' 父路由：/index.html 不匹配 path '/' → 抛 404 → 整个 App 不 mount
// - v13 v10 catch-all + v12 replaceState 双轨：history.replaceState 后 React 不重新渲染（throw 后不再 mount）
//
// v14 修：createHashRouter 用 URL hash 解析路径（不依赖 path 匹配）
// - WebView2 Navigate 到 https://flowring.local/index.html → HTML 加载 → React mount
// - createHashRouter 用 location.hash 解析（默认 #/，无 hash 时 → 默认 index）
// - 不需要 host 端 history.replaceState（v13 那条改动失效）
// - 不依赖 catch-all（v10 / v11 那条改动）
// - 不依赖 path '/'（v12 那条改动）
//
// NavLink to="/studio" 在 HashRouter 下自动变成 to="#/studio"，
// createHashRouter 解析 "#/studio" 匹配到对应子路由 → 渲染对应 Page。
// 简单、稳、不依赖任何路径匹配魔法。
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

// HashRouter 用 location.hash 解析，不依赖 path 匹配
const router = createHashRouter(routes);

const container = document.getElementById('root');
if (container === null) {
  throw new Error('未找到 #root 容器；index.html 必须包含 <div id="root"></div>。');
}

createRoot(container).render(
  <StrictMode>
    <RouterProvider router={router} />
  </StrictMode>,
);