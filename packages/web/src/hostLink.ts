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

/** 是否运行在快捷环弹窗窗体（宿主以 #overlay 打开）。 */
export function isPopupMode(): boolean {
  return typeof location !== 'undefined' && location.hash.includes('overlay');
}
