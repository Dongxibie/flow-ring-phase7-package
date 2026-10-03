import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { RouterProvider, createBrowserRouter, type RouteObject } from 'react-router-dom';
import { Layout } from './Layout';
import { ProfileManagerPage } from './pages/ProfileManager/ProfileManagerPage';
import { RingStudioPage } from './pages/RingStudio/RingStudioPage';
import { SettingsPage } from './pages/Settings/SettingsPage';
import { FlowCodePage } from './pages/FlowCode/FlowCodePage';

// v11 修法：children catch-all path '*' 兜底，让任意路径都能渲染 Page
//
// v10 修法回顾：
// - 顶层 path: '*' catch-all + Layout element
// - children 路径：index + 'studio' + 'profiles' + 'flow-code' + 'settings'
// - WebView2 Navigate 到 https://flowring.local/index.html → HTML 加载成功
// - 顶层 catch-all 匹配 → Layout 渲染 → 顶部 NavLink + Outlet
// - 但 children index 路径是父 '*' + '/'（即 '*'），实际路径 '/index.html' 不匹配
// - 实际行为：Layout 渲染成功 + Outlet 不渲染 → 主窗口顶部 OK，主体空白
//
// v11 修：children 末尾加 path: '*' catch-all
// - 任意不匹配的路径（如 /index.html、/index.html#studio 等）都进 catch-all child
// - 默认渲染 ProfileManagerPage（最常用页面）
// - 显式路径（/studio、/profiles、/flow-code、/settings）优先匹配各自子路由
// - 不依赖 host 端 URL 替换，纯粹前端路由修复
const routes: RouteObject[] = [
  {
    path: '*',
    element: <Layout />,
    children: [
      { index: true, element: <ProfileManagerPage /> },
      { path: 'studio', element: <RingStudioPage /> },
      { path: 'profiles', element: <ProfileManagerPage /> },
      { path: 'flow-code', element: <FlowCodePage /> },
      { path: 'settings', element: <SettingsPage /> },
      { path: '*', element: <ProfileManagerPage /> },
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