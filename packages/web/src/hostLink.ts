// v21：前端 → host 的低频指令通道（走 WebView2 postMessage；host 端 MainWindow /
// RingOverlayForm 的 WebMessageReceived 会路由到 HostController）。
// 不在 WebView2 里（dev server / 浏览器）时降级为 console.log，界面行为不变。
export function postHost(type: string, payload?: Record<string, unknown>): void {
  const body = JSON.stringify(payload ? { type, ...payload } : { type });
  if (typeof window !== 'undefined' && window.chrome?.webview) {
    try {
      window.chrome.webview.postMessage(body);
    } catch {
      // 静默失败
    }
  } else {
    console.log('[hostLink:stub]', body);
  }
}

/** 宿主 → 页面消息订阅（v23.1 修复）：优先 window.chrome.webview；浏览器调试时回退 window 的 message。返回取消订阅函数。 */
export function onHostMessage(handler: (data: { type?: string; [k: string]: unknown }) => void): () => void {
  const dispatch = (raw: unknown): void => {
    let data: unknown = raw;
    if (typeof data === 'string') {
      try {
        data = JSON.parse(data);
      } catch {
        return; // 非 JSON 字符串丢弃
      }
    }
    if (typeof data !== 'object' || data === null) {
      return; // 非对象丢弃
    }
    handler(data as { type?: string; [k: string]: unknown });
  };

  const webview = typeof window !== 'undefined' ? window.chrome?.webview : undefined;
  if (webview) {
    const h = (e: { data: string }): void => dispatch(e.data);
    webview.addEventListener('message', h);
    return () => {
      if (typeof webview.removeEventListener === 'function') {
        webview.removeEventListener('message', h);
      }
    };
  }

  if (typeof window === 'undefined') {
    return () => {};
  }
  const h = (e: MessageEvent): void => dispatch(e.data);
  window.addEventListener('message', h);
  return () => {
    window.removeEventListener('message', h);
  };
}

/** 是否运行在快捷环弹窗窗体（宿主以 #overlay 打开）。 */
export function isPopupMode(): boolean {
  return typeof location !== 'undefined' && location.hash.includes('overlay');
}
