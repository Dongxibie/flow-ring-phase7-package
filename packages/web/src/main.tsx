import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { RouterProvider, createBrowserRouter, type RouteObject } from 'react-router-dom';
import { Layout } from './Layout';
import { ProfileManagerPage } from './pages/ProfileManager/ProfileManagerPage';
import { RingStudioPage } from './pages/RingStudio/RingStudioPage';
import { SettingsPage } from './pages/Settings/SettingsPage';
import { FlowCodePage } from './pages/FlowCode/FlowCodePage';

// v10 修法：WebView2 Navigate 必须带文件名（/index.html），不能裸路径（/）
//
// v9 实测：WebView2 Navigate 到 https://flowring.local/ 报 ERR_ACCESS_DENIED
// - SetVirtualHostNameToFolderMapping 把 dist 映射成虚拟域
// - 但裸 / 路径在 dist 里没文件（只有 index.html 等）
// - SDK 1.0.2651.64 没有 defaultDocument 重载（reflection 验证）
// - WebView2 不会自动 fallback 到 index.html → ERR_ACCESS_DENIED
//
// v10 修：catch-all 顶层 + Layout element + children 相对路径
// - WebView2 Navigate 改回 https://flowring.local/index.html（确保 HTML 加载成功）
// - 顶层 path: '*' catch-all 让任何路径（/index.html、/studio 等）都进 Layout
// - children 路径相对 catch-all（'studio' 不是 '/studio'），匹配 React Router v6 嵌套规则
// - Outlet 渲染 children（按相对路径匹配）
// - /index.html 进入时，path: '*' 渲染 Layout，children index 默认匹配 → ProfileManagerPage
//
// 测试预期：
// - WebView2 Navigate 到 https://flowring.local/index.html → HTML 加载
// - 顶层 catch-all 匹配 → Layout 渲染
// - 主窗口看到顶部 NavLink 四条 + 主体 ProfileManagerPage 占位内容
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