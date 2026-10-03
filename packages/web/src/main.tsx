import { useState, useEffect } from 'react';
import { createRoot } from 'react-dom/client';
import { ProfileManagerPage } from './pages/ProfileManager/ProfileManagerPage';
import { RingStudioPage } from './pages/RingStudio/RingStudioPage';
import { FlowCodePage } from './pages/FlowCode/FlowCodePage';
import { SettingsPage } from './pages/Settings/SettingsPage';
import { useFlowStore } from './store/flowStore';
import './ui.css';

// v20：暗色编辑排版主题（AI空间样例 08/09 视觉稿定稿版）。
// 角落锚点布局：左上品牌 / 右上微文 / 中左大标题 / 左下导航 / 右下计数器；
// 页面内容在右栏（.page），环工作室独占整屏（.studio）。
//
// v18.5 的 postState 上报链路原样保留：host 的 WebMessageReceived 诊断依赖它，
// 前端 mount / 换页 / 错误都必须继续上报。
type PageName = 'profiles' | 'studio' | 'flow-code' | 'settings';

const PAGE_META: Record<PageName, {
  no: string; title: string; desc: string; blurb: string; counter: string; counterLabel: string;
}> = {
  profiles: {
    no: '01', title: '档案',
    desc: '每个档案保存独立的环布局与动作指派。\n切换档案，即切换整套快捷方式。',
    blurb: '按住触发键，环浮现于指尖。\n方向即动作，松开即执行。',
    counter: '', counterLabel: '档案总数',
  },
  studio: {
    no: '02', title: '环工作室',
    desc: '一个完整的环，劈成八个扇区。\n从动作库拖入，即完成指派。',
    blurb: '槽位指派即所见。',
    counter: '08', counterLabel: '方向',
  },
  'flow-code': {
    no: '03', title: '流码',
    desc: '把整套环配置折叠成一段可分享的码，\n也能从码还原。',
    blurb: '导出 · 导入 · 随身携带。',
    counter: '03', counterLabel: '流码',
  },
  settings: {
    no: '04', title: '设置',
    desc: '触发键、死区与动画——\n让环按你的手感浮现。',
    blurb: '让环按你的手感浮现。',
    counter: '04', counterLabel: '设置',
  },
};

// v18.5 修法：前端主动 postMessage 上报页面状态。
// v19 修过 API 名：真实 API 是 window.chrome.webview（没有 "2"）。
function postState(state: Record<string, unknown>): void {
  if (typeof window !== 'undefined' && window.chrome?.webview) {
    try {
      window.chrome.webview.postMessage(JSON.stringify({ type: 'PAGE_STATE', state }));
    } catch (e) {
      // 静默失败
    }
  }
}

function setupErrorReporting(): void {
  window.addEventListener('error', (e) => {
    postState({ kind: 'window.error', message: e.message, filename: e.filename, lineno: e.lineno });
  });
  window.addEventListener('unhandledrejection', (e) => {
    const reason = e.reason instanceof Error ? `${e.reason.message}\n${e.reason.stack}` : String(e.reason);
    postState({ kind: 'unhandledrejection', reason });
  });
  const origConsoleError = console.error.bind(console);
  console.error = (...args: unknown[]) => {
    postState({ kind: 'console.error', args: args.map(String) });
    origConsoleError(...args);
  };
}

function App(): JSX.Element {
  const [page, setPage] = useState<PageName>('profiles');
  const activeProfileId = useFlowStore((s: import('./store/flowStore').FlowState) => s.activeProfileId);
  const profiles = useFlowStore((s: import('./store/flowStore').FlowState) => s.profiles);

  useEffect(() => {
    setupErrorReporting();
    postState({ kind: 'app.mount', page, activeProfileId });
  }, []);

  useEffect(() => {
    postState({ kind: 'page.change', page, activeProfileId });
  }, [page, activeProfileId]);

  const meta = PAGE_META[page];
  const counter = page === 'profiles' ? String(profiles.length).padStart(2, '0') : meta.counter;

  return (
    <div className="stage">
      <header className="brand">
        <div className="wm">FLOW RING</div>
        <div className="sub">空间交互层</div>
      </header>
      <p className="blurb">{meta.blurb}</p>

      <section className={'hero' + (page === 'studio' ? ' compact' : '')}>
        <div className="eyebrow">{meta.no} · {meta.title}</div>
        <h1>{meta.title}</h1>
        <p className="desc">{meta.desc}</p>
      </section>

      <nav className="nav">
        {(Object.keys(PAGE_META) as PageName[]).map((k) => (
          <button key={k} type="button" className={page === k ? 'on' : ''} onClick={() => setPage(k)}>
            <span className="no">{PAGE_META[k].no}</span>{PAGE_META[k].title}
          </button>
        ))}
      </nav>

      <div className="counter">
        <div className="n">{counter}</div>
        <div className="l">{meta.counterLabel}</div>
      </div>

      {page === 'studio'
        ? <main className="studio"><RingStudioPage /></main>
        : (
          <main className="page">
            {page === 'profiles' && <ProfileManagerPage />}
            {page === 'flow-code' && <FlowCodePage />}
            {page === 'settings' && <SettingsPage />}
          </main>
        )}
    </div>
  );
}

const container = document.getElementById('root');
if (container === null) {
  throw new Error('未找到 #root 容器；index.html 必须包含 <div id="root"></div>。');
}

createRoot(container).render(<App />);
