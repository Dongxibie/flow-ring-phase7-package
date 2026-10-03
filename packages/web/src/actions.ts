// 动作定义（MVP 前端样例库；host 侧真实动作接入后以此字典做本地化展示）。
// v20.1：用户要求扇区里直接写功能名（"打开前端""静音"…），且全程序中英可切。
export interface ActionDef {
  code: string;
  zh: string;
  en: string;
  key: string;
}

export const ACTIONS: ActionDef[] = [
  { code: 'open-frontend', zh: '打开前端', en: 'Open Frontend', key: '' },
  { code: 'system-mute', zh: '静音', en: 'Mute', key: '' },
  { code: 'system-screenshot', zh: '截取屏幕', en: 'Screen Capture', key: 'Win+Shift+S' },
  { code: 'key-ctrl-shift-t', zh: '打开终端', en: 'Open Terminal', key: 'Ctrl+Shift+T' },
  { code: 'system-taskview', zh: '任务视图', en: 'Task View', key: 'Win+Tab' },
  { code: 'system-desktop', zh: '显示桌面', en: 'Show Desktop', key: 'Win+D' },
  { code: 'system-lock', zh: '锁定', en: 'Lock', key: 'Win+L' },
  { code: 'system-clipboard', zh: '剪贴板历史', en: 'Clipboard History', key: 'Win+V' },
];

export const ACTION_LIBRARY: string[] = ACTIONS.map((a) => a.code);

export function actionDef(code: string): ActionDef | null {
  return ACTIONS.find((a) => a.code === code) ?? null;
}

export function actionName(code: string, lang: 'zh' | 'en'): string {
  const d = actionDef(code);
  return d ? (lang === 'zh' ? d.zh : d.en) : code;
}

export function actionKey(code: string): string {
  return actionDef(code)?.key ?? '';
}
