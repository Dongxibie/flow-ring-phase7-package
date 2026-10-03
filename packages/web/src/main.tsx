import { useState } from 'react';
import { createRoot } from 'react-dom/client';
import { ProfileManagerPage } from './pages/ProfileManager/ProfileManagerPage';
import { RingStudioPage } from './pages/RingStudio/RingStudioPage';
import { SettingsPage } from './pages/Settings/SettingsPage';
import { FlowCodePage } from './pages/FlowCode/FlowCodePage';
import { useFlowStore } from './store/flowStore';

// v18 修法：彻底抛弃 React Router + HashRouter + catch-all + path match
//
// 大审查发现（v5-v17 35 commits 累积）：
// - 之前所有修复都在 React Router v6 路径匹配机制内部叠加假设
// - v14 HashRouter + v17 Navigate to="/" replace 在 dist 里都有，但白屏仍存在
// - 可能根因：ProfileManagerPage 内 useBridge() 抛错 / HashRouter 内部 pathname / hash 行为不一致
//
// v18 不再依赖 React Router 任何机制——vanilla useState 管 page：
// - 0 路径匹配
// - 0 hash 处理
// - 0 catch-all 嵌套
// - 0 Navigate 跳转
// - 0 react-router-dom 包依赖
//
// page 切换用 <button onClick> + useState 同步 setState，立即生效，无任何异步时序问题。
// NavLink 改 button（不依赖 NavLink 的 path-to-href 转换）。
// Layout 改 inline header 在 App 组件内（不嵌套 Outlet）。

type PageName = 'profiles' | 'studio' | 'flow-code' | 'settings';

function App(): JSX.Element {
  const [page, setPage] = useState<PageName>('profiles');
  const activeProfileId = useFlowStore((s: import('./store/flowStore').FlowState) => s.activeProfileId);

  return (
    <main lang="zh-CN" style={{ fontFamily: 'system-ui, sans-serif' }}>
      <header style={{ display: 'flex', gap: '12px', padding: '8px 16px', borderBottom: '1px solid #dadbe1' }}>
        <button
          type="button"
          onClick={() => setPage('profiles')}
          style={{ fontWeight: page === 'profiles' ? 'bold' : 'normal' }}
        >
          Profile Manager
        </button>
        <button
          type="button"
          onClick={() => setPage('studio')}
          style={{ fontWeight: page === 'studio' ? 'bold' : 'normal' }}
        >
          Studio
        </button>
        <button
          type="button"
          onClick={() => setPage('flow-code')}
          style={{ fontWeight: page === 'flow-code' ? 'bold' : 'normal' }}
        >
          Flow Code
        </button>
        <button
          type="button"
          onClick={() => setPage('settings')}
          style={{ fontWeight: page === 'settings' ? 'bold' : 'normal' }}
        >
          设置
        </button>
        <span style={{ marginLeft: 'auto', fontSize: '12px', opacity: 0.7 }}>
          当前 Profile：{activeProfileId}
        </span>
      </header>
      <section style={{ padding: '16px' }}>
        {page === 'profiles' && <ProfileManagerPage />}
        {page === 'studio' && <RingStudioPage />}
        {page === 'flow-code' && <FlowCodePage />}
        {page === 'settings' && <SettingsPage />}
      </section>
    </main>
  );
}

const container = document.getElementById('root');
if (container === null) {
  throw new Error('未找到 #root 容器；index.html 必须包含 <div id="root"></div>。');
}

createRoot(container).render(<App />);