import { StrictMode } from 'react';
import { createRoot } from 'react.dom/client';
import { RouterProvider, createBrowserRouter, type RouteObject } from 'react-router-dom';
import { Layout } from './Layout';
import { ProfileManagerPage } from './pages/ProfileManager/ProfileManagerPage';
import { RingStudioPage } from './pages/RingStudio/RingStudioPage';
import { SettingsPage } from './pages/Settings/SettingsPage';
import { FlowCodePage } from './pages/FlowCode/FlowCodePage';

// v12 修法：回退 v11 嵌套 catch-all，改用 v9 风格 path '/' 父路由
//
// v11 失败（实测用户白屏）：
// - 顶层 path: '*' catch-all + children 含 path: '*' catch-all（嵌套 catch-all）
// - React Router v6 nested catch-all 行为异常：父子两层都吃 '*'，路径匹配优先级冲突
// - 实测结果：连顶层 Layout 都不渲染（之前 v10 顶部 OK 都丢了）→ 回到初始白屏
//
// v12 修：去掉嵌套 catch-all，回退到 v9 风格
// - 顶层 path: '/' 父路由 + Layout element
// - children index + 4 个具体路径子路由（studio / profiles / flow-code / settings）
// - 让 v12 一定渲染：哪怕 children 都不匹配，Layout 顶部仍渲染（保证不退化到白屏）
//
// 配合 host 端：NavigationCompleted 后 ExecuteScriptAsync 把 URL 改 '/'
// - /index.html 加载后立即 history.replaceState({}, '', '/') + 触发 popstate
// - React Router 看到路径 '/' 匹配 path '/' 父 → children index 渲染 ProfileManagerPage
//
// 预期：
// - v11 实测白屏：✅ 修复回 v10 状态（顶部 Layout 渲染 + Outlet 渲染 ProfileManagerPage）
// - 主体：ProfileManagerPage 完整渲染（"Profile 管理" + "+ 新建 Profile" + Profile 卡片列表）
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