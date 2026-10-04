import { useCallback, useEffect, useRef, useState } from 'react';
import { SegmentedRing, type SegSlot } from './SegmentedRing';
import { useFlowStore } from '../store/flowStore';
import { useLang, getLang, t } from '../i18n';
import { actionName, actionKey } from '../actions';
import { useBridge } from '../bridge/useBridge';
import { loadSlots } from '../slotStore';
import { EIGHT_DIRECTIONS } from './SegmentedRing';
import { postHost, isPopupMode, onHostMessage } from '../hostLink';
import { resolveAction } from '../actions';
import { loadActiveProfileId } from '../profileStore';

// v20.1：右键唤起的运行时形态——只有单纯的圆环，功能标注在扇区里，
// 没有任何环外杂物。单击扇区 = 触发（MVP 仅关闭并回传日志）；ESC / 右键 / 点空白关闭。
// v23.1：死区半径改用最新设置（不再硬编码 56）。
function inferDirection(dx: number, dy: number, deadZoneRadiusPx: number): string | null {
  if (Math.sqrt(dx * dx + dy * dy) < deadZoneRadiusPx) {
    return null; // 死区（与 host 阈值一致）
  }
  let deg = Math.atan2(dy, dx) * 180 / Math.PI;
  if (deg < 0) {
    deg += 360;
  }
  const sector = Math.round(deg / 45) % 8;
  return ['Right', 'BottomRight', 'Bottom', 'BottomLeft', 'Left', 'TopLeft', 'Top', 'TopRight'][sector] ?? null;
}

export function OverlayRing({ epoch, onClose }: { epoch?: number; onClose: () => void }): JSX.Element {
  const lang = useLang();
  const settings = useFlowStore((s: import('../store/flowStore').FlowState) => s.settings);
  const bridge = useBridge();
  const [slots, setSlots] = useState<Record<string, SegSlot>>({});
  const [flash, setFlash] = useState<string>('');
  // v23：手势模式——按住时宿主回传光标相对【环心】的偏移，据此高亮方向；松开回传 RELEASE_SELECT 执行
  const [gestureDir, setGestureDir] = useState<string | null>(null);
  // v24：驻留模式悬停方向（与手势指向共用环心读数）
  const [hoverDir, setHoverDir] = useState<string | null>(null);

  // v23.1：宿主消息只订阅一次——最新 slots / onClose / requestClose 经 ref 读取，避免闭包过期
  const slotsRef = useRef(slots);

  useEffect(() => {
    slotsRef.current = slots;
  }, [slots]);

  // v23.1：epoch 每次 OVERLAY_ON 递增 → 重读槽位，弹窗指派保持新鲜
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
    // bridge 由 useBridge 的 useMemo 保证跨渲染引用稳定（依赖全是稳定回调），入依赖数组不改变重跑时机
  }, [bridge, epoch]);

  // v23.1：关闭路径统一——弹窗窗体先告知宿主隐藏，再通知应用内覆盖层收起
  const requestClose = useCallback(() => {
    if (isPopupMode()) {
      postHost('OVERLAY_DONE');
    }
    onClose();
  }, [onClose]);

  const requestCloseRef = useRef(requestClose);
  useEffect(() => {
    requestCloseRef.current = requestClose;
  }, [requestClose]);

  // v24：宿主主动收起（ESC / 点击环外）时只需重置页面状态，不必再回发 OVERLAY_DONE
  const onCloseRef = useRef(onClose);
  useEffect(() => {
    onCloseRef.current = onClose;
  }, [onClose]);

  useEffect(() => {
    const h = (e: KeyboardEvent): void => {
      if (e.key === 'Escape') {
        requestClose();
      }
    };
    window.addEventListener('keydown', h);
    return () => {
      window.removeEventListener('keydown', h);
    };
  }, [requestClose]);

  // v23：宿主消息（FOLLOW 跟随高亮 / RELEASE_SELECT 松开执行）
  // v23.1：改走 window.chrome.webview（WebView2 的宿主消息只派发到那里）
  useEffect(() => {
    return onHostMessage((data) => {
      if (data.type === 'FOLLOW' && typeof data.dx === 'number' && typeof data.dy === 'number') {
        const deadZone = useFlowStore.getState().settings.deadZoneRadiusPx;
        setGestureDir(inferDirection(data.dx, data.dy, deadZone));
      } else if (data.type === 'OVERLAY_CLOSE') {
        // v24：宿主侧已收起（ESC / 点击环外）——重置页面状态即可
        setGestureDir(null);
        setHoverDir(null);
        setFlash('');
        onCloseRef.current();
      } else if (data.type === 'RELEASE_SELECT') {
        const dir = typeof data.dir === 'string' ? data.dir : null;
        const slot = dir ? slotsRef.current[dir] : undefined;
        const ref = slot?.kind === 'action' ? slot.actionRef : undefined;
        const label = ref ? actionName(ref, getLang()) : t('空槽位', 'Empty');
        setFlash(label); // v23.1：只存标签，前缀在渲染处拼一次
        if (ref) {
          const resolved = resolveAction(ref);
          if (resolved) {
            postHost('ACTION_TRIGGER', { code: resolved.code, arg: resolved.arg });
          }
        }
        setGestureDir(null);
        window.setTimeout(() => {
          requestCloseRef.current();
        }, 500);
      }
    });
  }, []);

  // v21：单击扇区 = 真执行。ACTION_TRIGGER → host ActionDispatcher（SendInput / 启终端 / 打开主界面）。
  const trigger = (dir: string): void => {
    const slot = slots[dir];
    const ref = slot?.kind === 'action' ? slot.actionRef : undefined;
    const resolved = ref ? resolveAction(ref) : null;
    const label = ref ? actionName(ref, lang) : t('空槽位', 'Empty');
    setFlash(label);
    if (resolved) {
      postHost('ACTION_TRIGGER', { code: resolved.code, arg: resolved.arg });
    }
    window.setTimeout(requestClose, 500);
  };

  // v23.1：弹窗窗体直接用设置尺寸（不再压小）；应用内覆盖层保持原夹取
  const size = isPopupMode()
    ? settings.ringSizePx
    : Math.min(settings.ringSizePx, Math.round(window.innerHeight * 0.72), Math.round(window.innerWidth * 0.7));

  // v24：环心读数——当前指向的扇区（手势优先，其次悬停），一眼看清松开后会执行什么
  const activeDir = gestureDir ?? hoverDir;
  const activeSlot = activeDir !== null ? slots[activeDir] : undefined;
  const activeRef = activeSlot?.kind === 'action' ? activeSlot.actionRef : undefined;
  const activeName = activeDir !== null ? (activeRef ? actionName(activeRef, lang) : t('空槽位', 'Empty')) : '';
  const activeKey = activeRef ? actionKey(activeRef) : '';

  return (
    <div
      className="overlay"
      onClick={requestClose}
      onContextMenu={(e) => {
        e.preventDefault();
        requestClose();
      }}
    >
      <div className="ringwrap" onClick={(e) => e.stopPropagation()}>
        <SegmentedRing
          size={size}
          opacity={settings.ringOpacity}
          bgColor="#0b0c0a"
          darkGlass
          slots={slots}
          selected={null}
          hovered={gestureDir}
          onSelect={trigger}
          onHover={(d) => setHoverDir(d)}
          nameOf={(r) => actionName(r, lang)}
          keyOf={actionKey}
          emptyLabel={t('空槽位', 'Empty')}
          childLabel={(id) => (lang === 'zh' ? `子环 · ${id ?? '?'}` : `Sub-ring · ${id ?? '?'}`)}
          core={activeDir === null}
        />
        {activeDir !== null && (
          <div className="ring-readout">
            <div className="nm">{activeName}</div>
            {activeKey !== '' && <div className="ky">{activeKey}</div>}
          </div>
        )}
      </div>
      <p className="hint">
        {flash !== ''
          ? t(`已触发：${flash}`, `Triggered: ${flash}`)
          : t('单击扇区触发 · 右键或 ESC 关闭', 'Click a sector to trigger · right-click or ESC to close')}
      </p>
    </div>
  );
}
