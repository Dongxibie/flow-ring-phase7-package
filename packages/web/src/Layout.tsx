import { NavLink, Outlet, useLocation, Navigate } from 'react-router-dom';
import { useFlowStore } from './store/flowStore';

// v17 修法：Layout 内部用 <Navigate to="/" replace /> 强制跳转
//
// v14 HashRouter 实测白屏根因（大审查后定位）：
// - WebView2 Navigate /index.html，React mount 后 location.pathname = '/index.html'
// - React Router v6 HashRouter 在 initial load 时 path 用 location.pathname（不只用 hash）
// - 顶层 path '/' 不匹配 '/index.html' → Layout 不渲染？
//
// 等等：实测 v14 是 Layout 顶部 OK + Outlet 空白——说明 Layout 渲染了但 Outlet 没东西
// 真正根因：children index 默认路由期望 pathname = 父 path + '/' = '/' + '/' = '/'
// 但实际 pathname = '/index.html' → children index 不匹配 → Outlet 渲染 null
//
// v17 修：Layout mount 时如果 pathname 不是 '/'，<Navigate to="/" replace /> 强制 React Router
// 重新匹配 pathname '/'。replace=true 避免历史栈。
//
// 这是 React Router v6 官方推荐的处理 initial pathname 不匹配的方式。
// 不依赖 host 端 replaceState / popstate / hashchange 任何 hack。
const navStyle: React.CSSProperties = {
  display: 'flex',
  gap: '12px',
  padding: '8px 16px',
  borderBottom: '1px solid var(--fr-border, #dadbe1)',
};

export function Layout(): JSX.Element {
  const location = useLocation();

  // v17 关键修复：pathname 不是 '/' 时自动 redirect 到 '/'
  // 解决 v14 HashRouter 在 /index.html pathname 下 children index 不匹配的问题
  if (location.pathname !== '/' && location.pathname !== '/index.html') {
    // pathname 是其他路径（如 /studio）→ 不需要 redirect
  } else if (location.pathname === '/index.html') {
    // 强制跳到 '/'
    return <Navigate to="/" replace />;
  }

  return (
    <main lang="zh-CN" style={{ fontFamily: 'system-ui, sans-serif' }}>
      <header style={navStyle}>
        <NavLink to="/studio">Studio</NavLink>
        <NavLink to="/profiles">Profile Manager</NavLink>
        <NavLink to="/flow-code">Flow Code</NavLink>
        <NavLink to="/settings">设置</NavLink>
        <span style={{ marginLeft: 'auto', fontSize: '12px', opacity: 0.7 }}>
          当前 Profile：{useFlowStore.getState().activeProfileId}
        </span>
      </header>
      <Outlet />
    </main>
  );
}