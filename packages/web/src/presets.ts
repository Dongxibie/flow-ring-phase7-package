// v25：基础套装（先选套装，再自定义）。
// 每个套装 = 8 个方向的槽位指派（内置 code 或 custom:<id>）+ 若干自定义动作；
// 应用时一次性写入 localStorage（槽位按当前档案），之后用户可继续用动作库/属性面板自由编辑。
import {
  composeHotkeyCode,
  hotkeyLabel,
  loadCustomActions,
  saveCustomActions,
  type CustomAction,
} from './actions';
import { EIGHT_DIRECTIONS, type SegSlot } from './components/SegmentedRing';
import { loadActiveProfileId } from './profileStore';
import { saveSlots } from './slotStore';

export interface PackDef {
  id: 'desktop' | 'ide' | 'browser';
  name: string;
  desc: string;
  nameEn?: string;
  descEn?: string;
  slots: Record<string, string>; // 方向 → actionRef（内置 code 或 custom:<id>）
  customs: CustomAction[];
}

/** 快捷键类自定义动作：id 稳定（preset-<pack>-<key>），重复应用时按 id 覆盖而不是重复新增。 */
function hotkeyCustom(id: string, name: string, mods: string[], key: string): CustomAction {
  return { id, name, kind: 'key', code: composeHotkeyCode(mods, key), keyLabel: hotkeyLabel(mods, key) };
}

// ① 基础桌面：全部使用内置动作（与 ACTIONS 的 code 一一对应）。
const DESKTOP_PACK: PackDef = {
  id: 'desktop',
  name: '桌面',
  nameEn: 'Desktop',
  desc: '日常桌面动作：终端、截屏、任务视图、剪贴板等。',
  descEn: 'Everyday desktop actions: terminal, screenshot, task view, clipboard and more.',
  slots: {
    Top: 'open-frontend',
    TopRight: 'key-ctrl-shift-t',
    Right: 'system-screenshot',
    BottomRight: 'system-taskview',
    Bottom: 'system-desktop',
    BottomLeft: 'system-clipboard',
    Left: 'system-mute',
    TopLeft: 'system-lock',
  },
  customs: [],
};

// ② 基础 IDE：按 IntelliJ IDEA 默认键位（运行/调试是 Shift+F10 / Shift+F9）。
const IDE_PACK: PackDef = {
  id: 'ide',
  name: 'IDE',
  nameEn: 'IDE',
  desc: '按 IntelliJ IDEA 默认键位预设，可在下方「自定义」里修改。',
  descEn: 'IntelliJ IDEA default keymap; tweak any entry via Custom below.',
  slots: {
    Top: 'key-ctrl-shift-t',
    TopRight: 'custom:preset-ide-save',
    Right: 'custom:preset-ide-run',
    BottomRight: 'custom:preset-ide-debug',
    Bottom: 'custom:preset-ide-find',
    BottomLeft: 'custom:preset-ide-goto',
    Left: 'custom:preset-ide-format',
    TopLeft: 'open-frontend',
  },
  customs: [
    hotkeyCustom('preset-ide-save', '保存', ['ctrl'], 's'),
    hotkeyCustom('preset-ide-run', '运行', ['shift'], 'f10'),
    hotkeyCustom('preset-ide-debug', '调试', ['shift'], 'f9'),
    hotkeyCustom('preset-ide-find', '全局查找', ['ctrl', 'shift'], 'f'),
    hotkeyCustom('preset-ide-goto', '转到声明', ['ctrl'], 'b'),
    hotkeyCustom('preset-ide-format', '格式化', ['ctrl', 'alt'], 'l'),
  ],
};

// ③ 基础浏览器：按常见浏览器（Chrome/Edge）键位。
const BROWSER_PACK: PackDef = {
  id: 'browser',
  name: '浏览器',
  nameEn: 'Browser',
  desc: '按常见浏览器（Chrome/Edge）快捷键预设，可在下方「自定义」里修改。',
  descEn: 'Common browser shortcuts (Chrome/Edge); tweak any entry via Custom below.',
  slots: {
    Top: 'custom:preset-browser-newtab',
    TopRight: 'custom:preset-browser-closetab',
    Right: 'custom:preset-browser-reopen',
    BottomRight: 'custom:preset-browser-address',
    Bottom: 'custom:preset-browser-fullscreen',
    BottomLeft: 'system-screenshot',
    Left: 'system-clipboard',
    TopLeft: 'open-frontend',
  },
  customs: [
    hotkeyCustom('preset-browser-newtab', '新建标签页', ['ctrl'], 't'),
    hotkeyCustom('preset-browser-closetab', '关闭标签页', ['ctrl'], 'w'),
    hotkeyCustom('preset-browser-reopen', '恢复刚关闭', ['ctrl', 'shift'], 't'),
    hotkeyCustom('preset-browser-address', '地址栏', ['ctrl'], 'l'),
    hotkeyCustom('preset-browser-fullscreen', '全屏', [], 'f11'),
  ],
};

export const PACKS: PackDef[] = [DESKTOP_PACK, IDE_PACK, BROWSER_PACK];

export interface AppliedPack {
  slots: Record<string, SegSlot>;
  customs: CustomAction[];
}

/**
 * 应用套装：
 * 1) 把 pack.customs 合并进已有自定义动作（按 id 去重，套装版本覆盖同 id）并保存；
 * 2) 组装 8 个方向的 SegSlot 并写入当前档案的槽位；
 * 返回合并后的自定义动作与槽位，供页面立即刷新状态（动作库引用为 custom:<id>）。
 */
export function applyPack(pack: PackDef): AppliedPack {
  const merged = [...loadCustomActions()];
  for (const c of pack.customs) {
    const i = merged.findIndex((x) => x.id === c.id);
    if (i >= 0) {
      merged[i] = c;
    } else {
      merged.push(c);
    }
  }
  saveCustomActions(merged);

  const slots: Record<string, SegSlot> = {};
  for (const dir of EIGHT_DIRECTIONS) {
    const ref = pack.slots[dir];
    slots[dir] = ref ? { kind: 'action', actionRef: ref } : { kind: 'empty' };
  }
  saveSlots(slots, loadActiveProfileId());

  return { slots, customs: merged };
}
