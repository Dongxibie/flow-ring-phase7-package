import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { RouterProvider, createBrowserRouter, type RouteObject } from 'react-router-dom';
import { Layout } from './Layout';
import { ProfileManagerPage } from './pages/ProfileManager/ProfileManagerPage';
import { RingStudioPage } from './pages/RingStudio/RingStudioPage';
import { SettingsPage } from './pages/Settings/SettingsPage';
import { FlowCodePage } from './pages/FlowCode/FlowCodePage';

// v13 修法：恢复 v10 catch-all 顶层 + children index（**不带** children catch-all）
//
// v12 失败（实测用户白屏）：
// - v12 把 v10 的 path '*' catch-all 改回 path '/' 父路由
// - WebView2 Navigate /index.html，React Router 看到路径 /index.html
// - path '/' 不匹配 /index.html → 抛 ErrorResponse(404)
// - React 渲染错误 → 整个 App 没 mount → 主窗口完全空白（比 v11 截图更糟）
//
// v11 失败（实测用户白屏）：
// - v11 改 children 末尾加 path '*' catch-all（嵌套 catch-all）
// - React Router v6 嵌套 catch-all 行为异常 → Layout 也不渲染
//
// v13 修：v10 catch-all + v12 URL replaceState 双轨
// - 顶层 path: '*' catch-all + Layout element（任何路径都进 Layout，包括 /index.html）
// - children index（默认 ProfileManagerPage）+ 4 个具体子路由
// - **不带** children catch-all（避免 v11 嵌套 catch-all）
// - 配合 v12 host 端：NavigationCompleted 后 history.replaceState 把 URL 改 /
// - React Router 看到 '/' 匹配 catch-all → Layout 渲染 → children index ProfileManagerPage
//
// 预期 v13 主窗口：
// - 顶部 NavLink ✅
// - 右上角 当前 Profile 信息 ✅
// - 主体 ProfileManagerPage 完整渲染（"Profile 管理"标题 + Profile 卡片列表）
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