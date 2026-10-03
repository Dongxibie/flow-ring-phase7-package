import { useEffect, useMemo, useState } from 'react';
import { useBridge } from '../../bridge/useBridge';

// v20：环工作室改为"整环切割"形态（AI空间样例 09 视觉稿）——
// 一整块磨砂玻璃圆环被切缝劈成八份，激活扇区用楔形 clip-path 点亮；
// 动作名/快捷键作为水平标签挂在环外。拖拽指派、属性面板、保存链路原样保留。
interface SlotViewModel {
  direction: string;
  kind: 'empty' | 'action' | 'childRing';
  actionRef?: string;
  childRingId?: string;
}

const EIGHT_DIRECTIONS = ['Top', 'TopRight', 'Right', 'BottomRight', 'Bottom', 'BottomLeft', 'Left', 'TopLeft'];

const DIR_CN: Record<string, string> = {
  Top: '上', TopRight: '右上', Right: '右', BottomRight: '右下',
  Bottom: '下', BottomLeft: '左下', Left: '左', TopLeft: '左上',
};

// 已知动作的展示名与快捷键；未知动作码原样显示
const ACTION_INFO: Record<string, { name: string; key: string }> = {
  'key-ctrl-shift-t': { name: '打开终端', key: 'Ctrl+Shift+T' },
  'system-screenshot': { name: '截取屏幕', key: 'Win+Shift+S' },
};

// 楔形多边形：以"右"扇区为基准，屏幕坐标（y 向下）按 45° 旋转到各方向
const WEDGE: Record<string, string> = {
  Top: 'polygon(50% 50%, 32.1% 3.3%, 37.0% 1.7%, 42.2% 0.6%, 47.4% 0.1%, 52.6% 0.1%, 57.8% 0.6%, 63.0% 1.7%, 67.9% 3.3%)',
  TopRight: 'polygon(50% 50%, 70.4% 4.3%, 75.0% 6.7%, 79.4% 9.6%, 83.4% 12.9%, 87.1% 16.6%, 90.4% 20.6%, 93.3% 25.0%, 95.7% 29.6%)',
  Right: 'polygon(50% 50%, 96.7% 32.1%, 98.3% 37.0%, 99.4% 42.2%, 99.9% 47.4%, 99.9% 52.6%, 99.4% 57.8%, 98.3% 63.0%, 96.7% 67.9%)',
  BottomRight: 'polygon(50% 50%, 95.7% 70.4%, 93.3% 75.0%, 90.4% 79.4%, 87.1% 83.4%, 83.4% 87.1%, 79.4% 90.4%, 75.0% 93.3%, 70.4% 95.7%)',
  Bottom: 'polygon(50% 50%, 67.9% 96.7%, 63.0% 98.3%, 57.8% 99.4%, 52.6% 99.9%, 47.4% 99.9%, 42.2% 99.4%, 37.0% 98.3%, 32.1% 96.7%)',
  BottomLeft: 'polygon(50% 50%, 29.6% 95.7%, 25.0% 93.3%, 20.6% 90.4%, 16.6% 87.1%, 12.9% 83.4%, 9.6% 79.4%, 6.7% 75.0%, 4.3% 70.4%)',
  Left: 'polygon(50% 50%, 3.3% 67.9%, 1.7% 63.0%, 0.6% 57.8%, 0.1% 52.6%, 0.1% 47.4%, 0.6% 42.2%, 1.7% 37.0%, 3.3% 32.1%)',
  TopLeft: 'polygon(50% 50%, 4.3% 29.6%, 6.7% 25.0%, 9.6% 20.6%, 12.9% 16.6%, 16.6% 12.9%, 20.6% 9.6%, 25.0% 6.7%, 29.6% 4.3%)',
};

// 标签位置：环外半径 300px 处
const LABEL_POS: Record<string, { x: number; y: number }> = {
  Top: { x: 0, y: -300 },
  TopRight: { x: 212, y: -212 },
  Right: { x: 300, y: 0 },
  BottomRight: { x: 212, y: 212 },
  Bottom: { x: 0, y: 300 },
  BottomLeft: { x: -212, y: 212 },
  Left: { x: -300, y: 0 },
  TopLeft: { x: -212, y: -212 },
};

function actionInfo(ref?: string): { name: string; key: string } | null {
  if (!ref) {
    return null;
  }
  return ACTION_INFO[ref] ?? { name: ref, key: '' };
}

export function RingStudioPage(): JSX.Element {
  const bridge = useBridge();
  const [profileId] = useState<string>('default');
  const [slots, setSlots] = useState<Record<string, SlotViewModel>>({});
  const [actionLibrary, setActionLibrary] = useState<string[]>([]);
  const [selectedSlot, setSelectedSlot] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    void bridge.loadStudio(profileId).then(({ ringGraphJson, actionLibrary: lib }: { ringGraphJson: string; actionLibrary: string[] }) => {
      if (cancelled) {
        return;
      }
      try {
        const ring = JSON.parse(ringGraphJson) as { nodes: Record<string, { slots: Record<string, { kind: 'action' | 'childRing'; actionRef?: string; childRingId?: string }> }> };
        const root = ring.nodes.root;
        const mapped: Record<string, SlotViewModel> = {};
        for (const dir of EIGHT_DIRECTIONS) {
          const s = root.slots[dir];
          if (!s) {
            mapped[dir] = { direction: dir, kind: 'empty' };
          } else {
            mapped[dir] = {
              direction: dir,
              kind: s.kind === 'childRing' ? 'childRing' : 'action',
              actionRef: s.actionRef,
              childRingId: s.childRingId,
            };
          }
        }
        setSlots(mapped);
        setActionLibrary(lib);
      } catch (e: unknown) {
        setError(e instanceof Error ? e.message : 'Ring 图解析失败');
      }
    });
    return () => {
      cancelled = true;
    };
    // v19 白屏修复口诀：依赖收敛到稳定方法引用
  }, [bridge.loadStudio, profileId]);

  const actionSlotCount = useMemo(
    () => Object.values(slots).filter((s) => s.kind === 'action').length,
    [slots],
  );

  return (
    <div className="studio">
      {error !== null && <p className="err">{error}</p>}

      <div className="ringwrap">
        <div
          className="ring"
          onDragOver={(e) => e.preventDefault()}
          onDrop={(e) => {
            e.preventDefault();
            const actionRef = e.dataTransfer.getData('text/plain');
            const rect = (e.currentTarget as HTMLDivElement).getBoundingClientRect();
            const dx = e.clientX - (rect.left + rect.width / 2);
            const dy = e.clientY - (rect.top + rect.height / 2);
            const dir = inferDirection(dx, dy);
            if (dir !== null && actionRef.length > 0) {
              setSlots((prev) => ({
                ...prev,
                [dir]: { direction: dir, kind: 'action', actionRef },
              }));
              setSelectedSlot(dir);
            }
          }}
        >
          <div className="rg rg-glass" />
          <div className="rg rg-cuts" />
          {selectedSlot !== null && (
            <div className="rg rg-active" style={{ clipPath: WEDGE[selectedSlot] }} />
          )}
          <div className="rg-rim" />
          <div className="rg-rim-in" />
        </div>
        <div className="core"><i /></div>

        {EIGHT_DIRECTIONS.map((dir) => {
          const slot = slots[dir];
          const info = actionInfo(slot?.actionRef);
          const bound = slot?.kind === 'action' && info !== null;
          const on = selectedSlot === dir;
          const pos = LABEL_POS[dir];
          return (
            <div
              key={dir}
              className={'lbl' + (on ? ' on' : '')}
              style={{ left: pos.x, top: pos.y }}
              onClick={() => setSelectedSlot(dir)}
            >
              {on && (
                <div className="selframe">
                  <i className="sd tl" /><i className="sd tm" /><i className="sd tr" />
                  <i className="sd lm" /><i className="sd rm" />
                  <i className="sd bl" /><i className="sd bm" /><i className="sd br" />
                </div>
              )}
              <span className="d">{DIR_CN[dir]}</span>
              {bound ? (
                <>
                  <span className="nm">{info!.name}</span>
                  {info!.key !== '' && <span className="key">{info!.key}</span>}
                </>
              ) : slot?.kind === 'childRing' ? (
                <span className="nm">子环 · {slot.childRingId ?? '?'}</span>
              ) : (
                <span className="nm empty">空槽位</span>
              )}
            </div>
          );
        })}
      </div>

      <aside className="slotpanel">
        <div className="panel">
          <div className="ph">属性面板 · 已绑定 {actionSlotCount}/8</div>
          {selectedSlot === null ? (
            <p className="hint-p">点击环上的扇区或标签以编辑该方向。</p>
          ) : (
            <div>
              <p className="cur">方向：<strong>{DIR_CN[selectedSlot]}（{selectedSlot}）</strong></p>
              <select
                value={slots[selectedSlot]?.actionRef ?? ''}
                onChange={(e) => {
                  const v = e.target.value;
                  setSlots((prev) => ({
                    ...prev,
                    [selectedSlot]: v === ''
                      ? { direction: selectedSlot, kind: 'empty' }
                      : { direction: selectedSlot, kind: 'action', actionRef: v },
                  }));
                }}
              >
                <option value="">（空）</option>
                {actionLibrary.map((a) => (
                  <option key={a} value={a}>{a}</option>
                ))}
              </select>
              <div className="btnrow">
                <button
                  type="button"
                  className="fbtn"
                  onClick={() => {
                    const profileJson = JSON.stringify({ id: profileId, updatedAt: new Date().toISOString() });
                    const ringGraphJson = JSON.stringify({ nodes: { root: { slots: convertSlots(slots) } } });
                    void bridge.saveStudio({ profileId, profileJson, ringGraphJson });
                  }}
                >
                  保存
                </button>
                <button type="button" className="fbtn ghost" onClick={() => setSelectedSlot(null)}>取消</button>
              </div>
            </div>
          )}
        </div>

        <div className="panel">
          <div className="ph second">动作库 · 拖入环上指派</div>
          <ul className="lib">
            {actionLibrary.map((a) => (
              <li
                key={a}
                draggable
                onDragStart={(e) => e.dataTransfer.setData('text/plain', a)}
              >
                {a}
              </li>
            ))}
          </ul>
        </div>
      </aside>
    </div>
  );
}

function inferDirection(dx: number, dy: number): string | null {
  if (Math.hypot(dx, dy) < 16) {
    return null;
  }
  let deg = Math.atan2(dy, dx) * 180 / Math.PI;
  if (deg < 0) {
    deg += 360;
  }
  const sector = Math.round(deg / 45) % 8;
  return ['Right', 'BottomRight', 'Bottom', 'BottomLeft', 'Left', 'TopLeft', 'Top', 'TopRight'][sector] ?? null;
}

function convertSlots(slots: Record<string, SlotViewModel>): Record<string, { kind: 'action' | 'empty'; actionRef?: string }> {
  const out: Record<string, { kind: 'action' | 'empty'; actionRef?: string }> = {};
  for (const [dir, slot] of Object.entries(slots)) {
    if (slot.kind === 'action') {
      out[dir] = { kind: 'action', actionRef: slot.actionRef };
    } else {
      out[dir] = { kind: 'empty' };
    }
  }
  return out;
}
