// v21：槽位指派的本地持久化（localStorage）。
// MVP 阶段 host 侧档案库尚未接通前端，这里先让"工作室指派 → 覆盖层可见"成为真闭环；
// v1.1 接 FileSystemProfileStore 后，本模块可无缝替换为远端读写。
import type { SegSlot } from './components/SegmentedRing';
import { EIGHT_DIRECTIONS } from './components/SegmentedRing';

const KEY = 'flowring.slots';

export function loadSlots(): Record<string, SegSlot> {
  try {
    const raw = localStorage.getItem(KEY);
    if (raw) {
      const parsed = JSON.parse(raw) as Record<string, SegSlot>;
      const out: Record<string, SegSlot> = {};
      for (const dir of EIGHT_DIRECTIONS) {
        const s = parsed[dir];
        out[dir] = s?.kind === 'action' && s.actionRef ? s : { kind: 'empty' };
      }
      return out;
    }
  } catch {
    // 忽略
  }
  return {};
}

export function saveSlots(slots: Record<string, SegSlot>): void {
  try {
    localStorage.setItem(KEY, JSON.stringify(slots));
  } catch {
    // 忽略
  }
}
