import { useEffect, useState } from 'react';
import { SegmentedRing, type SegSlot } from './SegmentedRing';
import { useFlowStore } from '../store/flowStore';
import { useLang, t } from '../i18n';
import { actionName, actionKey } from '../actions';
import { useBridge } from '../bridge/useBridge';
import { loadSlots } from '../slotStore';
import { EIGHT_DIRECTIONS } from './SegmentedRing';
import { postHost, isPopupMode } from '../hostLink';
import { resolveAction } from '../actions';
import { loadActiveProfileId } from '../profileStore';

// v20.1：右键唤起的运行时形态——只有单纯的圆环，功能标注在扇区里，
// 没有任何环外杂物。单击扇区 = 触发（MVP 仅关闭并回传日志）；ESC / 右键 / 点空白关闭。
export function OverlayRing({ onClose }: { onClose: () => void }): JSX.Element {
  const lang = useLang();
  const settings = useFlowStore((s: import('../store/flowStore').FlowState) => s.settings);
  const bridge = useBridge();
  const [slots, setSlots] = useState<Record<string, SegSlot>>({});
  const [flash, setFlash] = useState<string>('');

  useEffect(() => {
    let cancelled = false;
    void bridge.loadStudio(loadActiveProfileId()).then(({ ringGraphJson }: { ringGraphJson: string }) => {
      if (cancelled) {
        return;
      }
      try {
        const ring = JSON.parse(ringGraphJson) as {
          nodes: Record<string, { slots: Record<string, SegSlot> }>;
        };
        const root = ring.nodes.root?.slots ?? {};
        const mapped: Record<string, SegSlot> = {};
        for (const dir of EIGHT_DIRECTIONS) {
          mapped[dir] = root[dir] ?? { kind: 'empty' };
        }
        // v21：工作室的指派（localStorage）优先
        const saved = loadSlots();
        for (const dir of EIGHT_DIRECTIONS) {
          if (saved[dir]?.kind === 'action') {
            mapped[dir] = saved[dir];
          }
        }
        setSlots(mapped);
      } catch {
        // 解析失败保持全空
      }
    });
    return () => {
      cancelled = true;
    };
  }, [bridge.loadStudio]);

  useEffect(() => {
    const h = (e: KeyboardEvent): void => {
      if (e.key === 'Escape') {
        onClose();
      }
    };
    window.addEventListener('keydown', h);
    return () => {
      window.removeEventListener('keydown', h);
    };
  }, [onClose]);

  // v21：单击扇区 = 真执行。ACTION_TRIGGER → host ActionDispatcher（SendInput / 启终端 / 打开主界面）。
  const trigger = (dir: string): void => {
    const slot = slots[dir];
    const ref = slot?.kind === 'action' ? slot.actionRef : undefined;
    const resolved = ref ? resolveAction(ref) : null;
    const label = ref ? actionName(ref, lang) : t('空槽位', 'Empty');
    setFlash(t(`已触发：${label}`, `Triggered: ${label}`));
    if (resolved) {
      postHost('ACTION_TRIGGER', { code: resolved.code, arg: resolved.arg });
    }
    window.setTimeout(() => {
      if (isPopupMode()) {
        postHost('OVERLAY_DONE'); // 快捷环弹窗窗体由宿主隐藏
      }
      onClose();
    }, 500);
  };

  return (
    <div
      className="overlay"
      onClick={onClose}
      onContextMenu={(e) => {
        e.preventDefault();
        onClose();
      }}
    >
      <div className="ringwrap" onClick={(e) => e.stopPropagation()}>
        <SegmentedRing
          size={Math.min(settings.ringSizePx, Math.round(window.innerHeight * 0.72), Math.round(window.innerWidth * 0.7))}
          opacity={settings.ringOpacity}
          bgColor="#0b0c0a"
          slots={slots}
          selected={null}
          onSelect={trigger}
          nameOf={(r) => actionName(r, lang)}
          keyOf={actionKey}
          emptyLabel={t('空槽位', 'Empty')}
          childLabel={(id) => (lang === 'zh' ? `子环 · ${id ?? '?'}` : `Sub-ring · ${id ?? '?'}`)}
        />
      </div>
      <p className="hint">
        {flash !== ''
          ? t(`已触发：${flash}`, `Triggered: ${flash}`)
          : t('单击扇区触发 · 右键或 ESC 关闭', 'Click a sector to trigger · right-click or ESC to close')}
      </p>
    </div>
  );
}
