// v22：档案的本地持久化（新建/重命名/复制/删除/设为当前全部落这里）。
// MVP 先以 localStorage 为准（与槽位同策略）；v1.1 接 host FileSystemProfileStore 后无缝替换。
export interface LocalProfile {
  id: string;
  name: string;
  isDefault: boolean;
}

const PROFILES_KEY = 'flowring.profiles';
const ACTIVE_KEY = 'flowring.activeProfile';

export function loadProfiles(): LocalProfile[] {
  try {
    const raw = localStorage.getItem(PROFILES_KEY);
    if (raw) {
      const parsed = JSON.parse(raw) as LocalProfile[];
      if (Array.isArray(parsed) && parsed.length > 0) {
        return parsed;
      }
    }
  } catch {
    // 忽略
  }
  return [{ id: 'default', name: '默认档案', isDefault: true }];
}

export function saveProfiles(list: LocalProfile[]): void {
  try {
    localStorage.setItem(PROFILES_KEY, JSON.stringify(list));
  } catch {
    // 忽略
  }
}

export function loadActiveProfileId(): string {
  try {
    return localStorage.getItem(ACTIVE_KEY) ?? 'default';
  } catch {
    return 'default';
  }
}

export function saveActiveProfileId(id: string): void {
  try {
    localStorage.setItem(ACTIVE_KEY, id);
  } catch {
    // 忽略
  }
}

export function newProfileId(): string {
  return 'p-' + Date.now().toString(36);
}
