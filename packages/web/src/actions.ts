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

// ── v22：自定义动作（用户自己命名 + 自己定义功能）──
// kind 'key'：code 形如 key-ctrl-alt-t（host 键盘执行器直接解析）
// kind 'app'：code 固定 app-launch，arg 为 exe 路径或网址（host Process.Start）
export interface CustomAction {
  id: string;
  name: string;
  kind: 'key' | 'app';
  code: string;
  arg?: string;
  keyLabel?: string; // 快捷键类动作的展示用键位
}

const CUSTOM_KEY = 'flowring.customActions';

export function loadCustomActions(): CustomAction[] {
  try {
    const raw = localStorage.getItem(CUSTOM_KEY);
    if (raw) {
      const parsed = JSON.parse(raw) as CustomAction[];
      if (Array.isArray(parsed)) {
        return parsed;
      }
    }
  } catch {
    // 忽略
  }
  return [];
}

export function saveCustomActions(list: CustomAction[]): void {
  try {
    localStorage.setItem(CUSTOM_KEY, JSON.stringify(list));
  } catch {
    // 忽略
  }
}

export function newCustomAction(name: string, kind: 'key' | 'app', param: string): CustomAction {
  const id = 'c-' + Date.now().toString(36);
  if (kind === 'key') {
    // "Ctrl+Alt+T" → key-ctrl-alt-t（host 键盘执行器按 '-' 分词解析）
    const tokens = param
      .split('+')
      .map((s) => s.trim())
      .filter(Boolean)
      .map((s) => s.toLowerCase());
    return { id, name, kind, code: ['key', ...tokens].join('-'), keyLabel: param.trim() };
  }
  return { id, name, kind, code: 'app-launch', arg: param.trim() };
}

// ── v25：自定义快捷键构建器 ──
// 由 UI 的「修饰键按钮 + 主键输入」生成 host 可解析的 code 与展示串。
// 修饰键固定顺序 ctrl,shift,alt,win；主键 token 统一小写（具名键 esc/f5/up…），
// 单字符主键（A–Z / 0–9 / 符号键）展示时字母转大写。
const MOD_ORDER: string[] = ['ctrl', 'shift', 'alt', 'win'];
const MOD_LABEL: Record<string, string> = { ctrl: 'Ctrl', shift: 'Shift', alt: 'Alt', win: 'Win' };
const NAMED_KEY_LABEL: Record<string, string> = {
  esc: 'Esc', enter: 'Enter', tab: 'Tab', space: 'Space',
  up: 'Up', down: 'Down', left: 'Left', right: 'Right',
  f1: 'F1', f2: 'F2', f3: 'F3', f4: 'F4', f5: 'F5', f6: 'F6',
  f7: 'F7', f8: 'F8', f9: 'F9', f10: 'F10', f11: 'F11', f12: 'F12',
};

function hasOwnKey(table: Record<string, string>, key: string): boolean {
  return Object.prototype.hasOwnProperty.call(table, key);
}

/** 组合快捷键 code：'key-' + 修饰键（固定顺序 ctrl,shift,alt,win）+ 主键 token。例：Ctrl+Shift+T → 'key-ctrl-shift-t'。 */
export function composeHotkeyCode(mods: string[], key: string): string {
  const ordered = MOD_ORDER.filter((m) => mods.some((x) => x.toLowerCase() === m));
  const token = key.trim().toLowerCase();
  return ['key', ...ordered, ...(token === '' ? [] : [token])].join('-');
}

/** 展示用键位串：例 Alt+T → 'Alt+T'、Shift+F10 → 'Shift+F10'、Ctrl+Shift+T → 'Ctrl+Shift+T'。 */
export function hotkeyLabel(mods: string[], key: string): string {
  const ordered = MOD_ORDER.filter((m) => mods.some((x) => x.toLowerCase() === m));
  const s = key.trim();
  const lower = s.toLowerCase();
  const display = hasOwnKey(NAMED_KEY_LABEL, lower)
    ? NAMED_KEY_LABEL[lower]
    : s.length === 1 && /[a-z]/i.test(s)
      ? s.toUpperCase()
      : s;
  return [...ordered.map((m) => MOD_LABEL[m]), display].filter((x) => x !== '').join('+');
}

/** 槽位 actionRef 的解析结果：真发给 host 的 code + 可选参数。 */
export interface ResolvedAction {
  code: string;
  arg?: string;
}

export function resolveAction(ref: string): ResolvedAction | null {
  if (ref.startsWith('custom:')) {
    const id = ref.slice('custom:'.length);
    const d = loadCustomActions().find((c) => c.id === id);
    return d ? { code: d.code, arg: d.arg } : null;
  }
  return { code: ref };
}

export function actionName(code: string, lang: 'zh' | 'en'): string {
  if (code.startsWith('custom:')) {
    const id = code.slice('custom:'.length);
    const d = loadCustomActions().find((c) => c.id === id);
    if (d) {
      return d.name;
    }
    return lang === 'zh' ? '（已删除的自定义动作）' : '(deleted custom action)';
  }
  const def = ACTIONS.find((a) => a.code === code);
  return def ? (lang === 'zh' ? def.zh : def.en) : code;
}

export function actionKeyLabel(code: string): string {
  if (code.startsWith('custom:')) {
    const id = code.slice('custom:'.length);
    return loadCustomActions().find((c) => c.id === id)?.keyLabel ?? '';
  }
  return actionDef(code)?.key ?? '';
}

export function actionKey(code: string): string {
  return actionKeyLabel(code);
}
