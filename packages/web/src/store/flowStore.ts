import { create } from 'zustand';

export interface ProfileSummary {
  id: string;
  name: string;
  isDefault: boolean;
}

export interface SettingsDraft {
  triggerKey: string;
  deadZoneRadiusPx: number;
  animationDurationMs: number;
  theme: 'light' | 'dark' | 'auto';
  ringOpacity: number; // 0.25~1，整环玻璃不透明度倍率（v20.1 新增）
  ringSizePx: number; // 320~640，环外直径（v20.1 新增）
}

export interface FlowState {
  isPaused: boolean;
  activeProfileId: string;
  profiles: ProfileSummary[];
  settings: SettingsDraft;

  setPaused: (paused: boolean) => void;
  setActiveProfile: (id: string) => void;
  setProfiles: (profiles: ProfileSummary[]) => void;
  updateSettings: (patch: Partial<SettingsDraft>) => void;
}

// v20.1：环透明度/大小持久化到 localStorage，设置页滑杆与环组件共用这一份。
const RING_KEY = 'flowring.ring';

function clamp(v: number, lo: number, hi: number): number {
  return Math.min(hi, Math.max(lo, v));
}

type RingPrefs = { ringOpacity: number; ringSizePx: number; deadZoneRadiusPx: number };

// v23.1：死区半径一并落 flowring.ring；旧数据没有该字段时用缺省值（向后兼容）。
function loadRingPrefs(): RingPrefs {
  const d: RingPrefs = { ringOpacity: 0.7, ringSizePx: 500, deadZoneRadiusPx: 56 };
  try {
    const raw = localStorage.getItem(RING_KEY);
    if (raw) {
      const o = JSON.parse(raw) as Partial<RingPrefs>;
      return {
        ringOpacity: clamp(typeof o.ringOpacity === 'number' ? o.ringOpacity : d.ringOpacity, 0.25, 1),
        ringSizePx: clamp(typeof o.ringSizePx === 'number' ? o.ringSizePx : d.ringSizePx, 320, 640),
        deadZoneRadiusPx: clamp(typeof o.deadZoneRadiusPx === 'number' ? o.deadZoneRadiusPx : d.deadZoneRadiusPx, 10, 120),
      };
    }
  } catch {
    // 忽略
  }
  return d;
}

const ringPrefs = loadRingPrefs();

const defaultSettings: SettingsDraft = {
  triggerKey: 'MouseSideButton',
  deadZoneRadiusPx: ringPrefs.deadZoneRadiusPx,
  animationDurationMs: 180,
  theme: 'auto',
  ringOpacity: ringPrefs.ringOpacity,
  ringSizePx: ringPrefs.ringSizePx,
};

export const useFlowStore = create<FlowState>((set) => ({
  isPaused: false,
  activeProfileId: 'default',
  profiles: [],
  settings: defaultSettings,

  setPaused: (paused) => set({ isPaused: paused }),
  setActiveProfile: (id) => set({ activeProfileId: id }),
  setProfiles: (profiles) => set({ profiles }),
  updateSettings: (patch) =>
    set((state) => {
      const settings = { ...state.settings, ...patch };
      try {
        localStorage.setItem(
          RING_KEY,
          JSON.stringify({
            ringOpacity: settings.ringOpacity,
            ringSizePx: settings.ringSizePx,
            deadZoneRadiusPx: settings.deadZoneRadiusPx,
          }),
        );
      } catch {
        // 忽略
      }
      return { settings };
    }),
}));

/** v23.1：从 localStorage 重读环偏好并合并进 settings（宿主 OVERLAY_ON 后调用，弹窗每次弹出取最新值）。 */
export function refreshRingPrefs(): void {
  const prefs = loadRingPrefs();
  useFlowStore.setState((state) => ({ settings: { ...state.settings, ...prefs } }));
}
