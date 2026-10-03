import { NavLink, Outlet, Route, Routes } from 'react-router-dom';
import { ProfileManagerPage } from './pages/ProfileManager/ProfileManagerPage';
import { RingStudioPage } from './pages/RingStudio/RingStudioPage';
import { SettingsPage } from './pages/Settings/SettingsPage';
import { FlowCodePage } from './pages/FlowCode/FlowCodePage';
import { useFlowStore } from './store/flowStore';

// v8 修复：路径从绝对路径（/studio）改成相对路径（studio）。
// React Router v6 在 <Routes> 内嵌套 <Route> 时，子 Route path 必须是相对路径，
// 否则抛出 "Absolute route path inside another <Route>" 错误或者不匹配。
//
// 配套的 main.tsx 改成 path: '*' catch-all（让任何路径都进 App），
// 这里 App 内部 <Routes> 接管路径匹配。
const navStyle: React.CSSProperties = {
  display: 'flex',
  gap: '12px',
  padding: '8px 16px',
  borderBottom: '1px solid var(--fr-border, #dadbe1)',
};

function Layout(): JSX.Element {
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

export function App(): JSX.Element {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<ProfileManagerPage />} />
        <Route path="studio" element={<RingStudioPage />} />
        <Route path="profiles" element={<ProfileManagerPage />} />
        <Route path="flow-code" element={<FlowCodePage />} />
        <Route path="settings" element={<SettingsPage />} />
      </Route>
    </Routes>
  );
}