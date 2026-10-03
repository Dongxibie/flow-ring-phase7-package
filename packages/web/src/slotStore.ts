// v21：槽位指派持久化（localStorage，按档案分键）。
// v22：每个档案独立一套环指派（跟 profileStore 的 activeProfileId 对应）。
// MVP 本地存储；v1.1 接 FileSystemProfileStore 后无缝替换。
import type { SegSlot } from './components/SegmentedRing';
import { EIGHT_DIRECTIONS } from './components/SegmentedRing';
import { loadActiveProfileId } from './profileStore';

const KEY_PREFIX = 'flowring.slots.';

export function loadSlots(profileId?: string): Record<string, SegSlot> {
  const pid = profileId ?? loadActiveProfileId();
  try {
    const raw = localStorage.getItem(KEY_PREFIX + pid);
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

export function saveSlots(slots: Record<string, SegSlot>, profileId?: string): void {
  const pid = profileId ?? loadActiveProfileId();
  try {
    localStorage.setItem(KEY_PREFIX + pid, JSON.stringify(slots));
  } catch {
    // 忽略
  }
}

export function removeSlots(profileId: string): void {
  try {
    localStorage.removeItem(KEY_PREFIX + profileId);
  } catch {
    // 忽略
  }
}
