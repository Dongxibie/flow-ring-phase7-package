import { useState, useEffect } from 'react';
import { createRoot } from 'react-dom/client';
import { ProfileManagerPage } from './pages/ProfileManager/ProfileManagerPage';
import { RingStudioPage } from './pages/RingStudio/RingStudioPage';
import { FlowCodePage } from './pages/FlowCode/FlowCodePage';
import { SettingsPage } from './pages/Settings/SettingsPage';
import { OverlayRing } from './components/OverlayRing';
import { useFlowStore } from './store/flowStore';
import { useLang, setLang, getLang, t } from './i18n';
import './ui.css';

// v20.1：暗色编辑排版主题 + 中英双语 + 右键覆盖层（单纯圆环形态）。
// 角落锚点布局：左上品牌 / 右上语言切换与微文 / 中左大标题 / 左下导航 / 右下计数器。
// v18.5 的 postState 上报链路原样保留：host 的 WebMessageReceived 诊断依赖它。
type PageName = 'profiles' | 'studio' | 'flow-code' | 'settings';

function pageMeta(page: PageName): {
  no: string; title: string; desc: string; blurb: string; counter: string; counterLabel: string;
} {
  const zh = getLangZh();
  switch (page) {
    case 'profiles':
      return {
        no: '01', title: t('档案', 'Profiles'),
        desc: zh
          ? '每个档案保存独立的环布局与动作指派。\n切换档案，即切换整套快捷方式。'
          : 'Each profile holds its own ring layout and bindings.\nSwitching a profile switches the whole shortcut set.',
        blurb: zh
          ? '按住触发键，环浮现于指尖。\n方向即动作，松开即执行。'
          : 'Hold the trigger and the ring appears\nat your fingertip. Direction is action.',
        counter: '', counterLabel: t('档案总数', 'PROFILES'),
      };
    case 'studio':
      return {
        no: '02', title: t('环工作室', 'Ring Studio'),
        desc: zh
          ? '一个完整的环，劈成八个扇区。\n从动作库拖入，即完成指派。'
          : 'One full ring, cut into eight sectors.\nDrag from the library to assign.',
        blurb: zh ? '槽位指派即所见。' : 'What you assign is what you see.',
        counter: '08', counterLabel: t('方向', 'DIRECTIONS'),
      };
    case 'flow-code':
      return {
        no: '03', title: t('流码', 'Flow Code'),
        desc: zh
          ? '把整套环配置折叠成一段可分享的码，\n也能从码还原。'
          : 'Fold the whole ring config into a shareable code,\nor restore from one.',
        blurb: zh ? '导出 · 导入 · 随身携带。' : 'Export · Import · Carry with you.',
        counter: '03', counterLabel: t('流码', 'FLOW CODE'),
      };
    case 'settings':
      return {
        no: '04', title: t('设置', 'Settings'),
        desc: zh
          ? '触发键、死区与动画——\n以及环的透明度与大小。'
          : 'Trigger, dead zone, animation —\nplus ring opacity and size.',
        blurb: zh ? '让环按你的手感浮现。' : 'Make the ring feel like yours.',
        counter: '04', counterLabel: t('设置', 'SETTINGS'),
      };
  }
}

function getLangZh(): boolean {
  return getLang() === 'zh';
}

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
  const lang = useLang();
  const [page, setPage] = useState<PageName>('profiles');
  // v21：快捷环弹窗窗体以 #overlay 打开——自动进入覆盖层模式（只有单纯的圆环）
  const [overlay, setOverlay] = useState(() => location.hash.includes('overlay'));
  const activeProfileId = useFlowStore((s: import('./store/flowStore').FlowState) => s.activeProfileId);
  const profiles = useFlowStore((s: import('./store/flowStore').FlowState) => s.profiles);

  useEffect(() => {
    setupErrorReporting();
    postState({ kind: 'app.mount', page, activeProfileId });
  }, []);

  useEffect(() => {
    postState({ kind: 'page.change', page, activeProfileId });
  }, [page, activeProfileId]);

  const meta = pageMeta(page);
  const counter = page === 'profiles' ? String(profiles.length).padStart(2, '0') : meta.counter;

  return (
    <div
      className="stage"
      onContextMenu={(e) => {
        // v20.1：右键唤起运行时圆环（应用内预览；全局触发键在 host 侧，v1.1 接通）
        e.preventDefault();
        setOverlay(true);
      }}
    >
      <header className="brand">
        <div className="wm">FLOW RING</div>
        <div className="sub">{t('空间交互层', 'SPATIAL LAYER')}</div>
      </header>

      <button
        type="button"
        className="langtgl"
        onClick={() => setLang(lang === 'zh' ? 'en' : 'zh')}
        title={t('切换到英文', '切换到中文')}
      >
        {lang === 'zh' ? 'EN' : '中文'}
      </button>
      <p className="blurb">{meta.blurb}</p>

      <section className={'hero' + (page === 'studio' ? ' compact' : '')}>
        <div className="eyebrow">{meta.no} · {meta.title}</div>
        <h1>{meta.title}</h1>
        <p className="desc">{meta.desc}</p>
      </section>

      <nav className="nav">
        {([
          ['profiles', t('档案', 'Profiles')],
          ['studio', t('环工作室', 'Ring Studio')],
          ['flow-code', t('流码', 'Flow Code')],
          ['settings', t('设置', 'Settings')],
        ] as [PageName, string][]).map(([k, label], i) => (
          <button key={k} type="button" className={page === k ? 'on' : ''} onClick={() => setPage(k)}>
            <span className="no">{'0' + (i + 1)}</span>{label}
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

      {overlay && <OverlayRing onClose={() => setOverlay(false)} />}
    </div>
  );
}

const container = document.getElementById('root');
if (container === null) {
  throw new Error('未找到 #root 容器；index.html 必须包含 <div id="root"></div>。');
}

createRoot(container).render(<App />);
