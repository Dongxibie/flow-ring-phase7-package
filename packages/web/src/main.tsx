import { useState, useEffect } from 'react';
import { createRoot } from 'react-dom/client';
import { ProfileManagerPage } from './pages/ProfileManager/ProfileManagerPage';
import { RingStudioPage } from './pages/RingStudio/RingStudioPage';
import { SettingsPage } from './pages/Settings/SettingsPage';
import { FlowCodePage } from './pages/FlowCode/FlowCodePage';
import { useFlowStore } from './store/flowStore';

// v18.5 修法：前端主动 postMessage 上报页面状态
//
// v18.3 fire-and-forget Task.Run 在 ThreadPool 上访问 _webView.CoreWebView2 → 抛
// System.InvalidOperationException: 'CoreWebView2 can only be accessed from the UI thread'
//
// v18.4 全局异常 handler 抓到了，但 v18.4 修法本身失败（dump 不能 host 主动调）。
//
// v18.5 改法：使用 WebView2 推荐的双向通信模式
// - 前端 useEffect 在 mount 后调 window.chrome.webview2.postMessage(JSON.stringify({type:'PAGE_STATE', state:{...}}))
// - host 监听 CoreWebView2.WebMessageReceived event handler
// - 通信永远由前端发起，host 只监听事件——彻底避免 host 主动访问 CoreWebView2 在非 STA 线程
//
// 还用了所有 console.error / window.onerror 拦截并通过 postMessage 上报：
// - 前端 JS 错误 → host log（不用 ConsoleMessage 监听，SDK 1.0.2792 不支持）
type PageName = 'profiles' | 'studio' | 'flow-code' | 'settings';

function postState(state: Record<string, unknown>): void {
  if (typeof window !== 'undefined' && window.chrome?.webview2) {
    try {
      window.chrome.webview2.postMessage(JSON.stringify({ type: 'PAGE_STATE', state }));
    } catch (e) {
      // 静默失败
    }
  }
}

function setupErrorReporting(): void {
  // window.onerror
  window.addEventListener('error', (e) => {
    postState({ kind: 'window.error', message: e.message, filename: e.filename, lineno: e.lineno });
  });
  // unhandledrejection
  window.addEventListener('unhandledrejection', (e) => {
    const reason = e.reason instanceof Error ? `${e.reason.message}\n${e.reason.stack}` : String(e.reason);
    postState({ kind: 'unhandledrejection', reason });
  });
  // console.error
  const origConsoleError = console.error.bind(console);
  console.error = (...args: unknown[]) => {
    postState({ kind: 'console.error', args: args.map(String) });
    origConsoleError(...args);
  };
}

function App(): JSX.Element {
  const [page, setPage] = useState<PageName>('profiles');
  const activeProfileId = useFlowStore((s: import('./store/flowStore').FlowState) => s.activeProfileId);

  // v18.5：App mount 后主动上报状态
  useEffect(() => {
    setupErrorReporting();
    postState({ kind: 'app.mount', page, activeProfileId });
  }, []);

  // v18.5：page 切换时上报
  useEffect(() => {
    postState({ kind: 'page.change', page, activeProfileId });
  }, [page, activeProfileId]);

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