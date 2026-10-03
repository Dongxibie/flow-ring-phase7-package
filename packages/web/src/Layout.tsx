import { NavLink, Outlet } from 'react-router-dom';
import { useFlowStore } from './store/flowStore';

// v9 重构：Layout 从原 App.tsx 抽出，作为路由表单一 Router 下的 Layout 组件。
// 通过 <Outlet /> 渲染 children 子路由（ProfileManagerPage / RingStudioPage 等）。
const navStyle: React.CSSProperties = {
  display: 'flex',
  gap: '12px',
  padding: '8px 16px',
  borderBottom: '1px solid var(--fr-border, #dadbe1)',
};

export function Layout(): JSX.Element {
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