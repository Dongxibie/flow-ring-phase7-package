import { useEffect, useMemo, useState } from 'react';
import { useBridge } from '../../bridge/useBridge';

interface SlotViewModel {
  direction: string;
  kind: 'empty' | 'action' | 'childRing';
  actionRef?: string;
  childRingId?: string;
}

const EIGHT_DIRECTIONS = ['Top', 'TopRight', 'Right', 'BottomRight', 'Bottom', 'BottomLeft', 'Left', 'TopLeft'];

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
    // v19 白屏根因修复：同 ProfileManagerPage，依赖收敛到稳定的方法引用
  }, [bridge.loadStudio, profileId]);

  const actionSlotCount = useMemo(
    () => Object.values(slots).filter((s) => s.kind === 'action').length,
    [slots],
  );

  return (
    <section style={{ padding: '16px', display: 'grid', gridTemplateColumns: '240px 1fr 280px', gap: '16px', minHeight: '70vh' }}>
      {/* 左栏：Action 库 */}
      <aside style={{ borderRight: '1px solid var(--fr-border, #dadbe1)', paddingRight: '12px' }}>
        <h3>Action 库</h3>
        <ul style={{ listStyle: 'none', padding: 0 }}>
          {actionLibrary.map((a) => (
            <li
              key={a}
              style={{
                border: '1px solid var(--fr-border, #dadbe1)',
                borderRadius: '8px',
                padding: '8px',
                marginBottom: '6px',
                cursor: 'grab',
                fontFamily: 'monospace',
              }}
              draggable
              onDragStart={(e) => e.dataTransfer.setData('text/plain', a)}
            >
              {a}
            </li>
          ))}
        </ul>
      </aside>

      {/* 中栏：Ring 实时预览 */}
      <main style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center' }}>
        <h3>Ring 预览 · 已绑定 {actionSlotCount}/8 方向</h3>
        {error !== null && <p style={{ color: 'crimson' }}>{error}</p>}
        <div
          style={{
            width: '320px',
            height: '320px',
            borderRadius: '50%',
            border: '2px dashed var(--fr-border, #dadbe1)',
            position: 'relative',
          }}
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
          {EIGHT_DIRECTIONS.map((dir) => {
            const slot = slots[dir];
            const isBound = slot?.kind === 'action';
            const angle = angleFor(dir);
            const radius = 110;
            const x = Math.cos(angle) * radius;
            const y = Math.sin(angle) * radius;
            return (
              <div
                style={{
                  position: 'absolute',
                  left: `calc(50% + ${x}px - 36px)`,
                  top: `calc(50% + ${y}px - 36px)`,
                  width: '72px',
                  height: '72px',
                  borderRadius: '50%',
                  background: selectedSlot === dir
                      ? 'rgba(0, 120, 255, 0.2)'
                      : (isBound ? 'rgba(0, 180, 0, 0.2)' : 'rgba(0, 0, 0, 0.05)'),
                  border: `2px solid ${selectedSlot === dir ? '#0078ff' : (isBound ? '#00b400' : '#dadbe1')}`,
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  fontSize: '11px',
                  cursor: 'pointer',
                  textAlign: 'center',
                  wordBreak: 'break-all',
                }}
                key={dir}
                onClick={() => setSelectedSlot(dir)}
              >
                <span>
                  <strong>{dir}</strong>
                  <br />
                  {isBound ? (slot?.actionRef ?? '') : '空'}
                </span>
              </div>
            );
          })}
        </div>
      </main>

      {/* 右栏：属性面板 */}
      <aside style={{ borderLeft: '1px solid var(--fr-border, #dadbe1)', paddingLeft: '12px' }}>
        <h3>属性面板</h3>
        {selectedSlot === null ? (
          <p>选择左侧 Ring 上的方向槽位以编辑。</p>
        ) : (
          <div>
            <p>方向：<strong>{selectedSlot}</strong></p>
            <p>当前绑定：</p>
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
            <div style={{ marginTop: '16px', display: 'flex', gap: '8px' }}>
              <button
                type="button"
                onClick={() => {
                  const profileJson = JSON.stringify({ id: profileId, updatedAt: new Date().toISOString() });
                  const ringGraphJson = JSON.stringify({ nodes: { root: { slots: convertSlots(slots) } } });
                  void bridge.saveStudio({ profileId, profileJson, ringGraphJson });
                }}
              >
                保存
              </button>
              <button type="button" onClick={() => setSelectedSlot(null)}>取消</button>
            </div>
          </div>
        )}
      </aside>
    </section>
  );
}

function angleFor(dir: string): number {
  // Top=-90°, Right=0°, Bottom=+90°, Left=180°
  // 8 方向按 45° 等分
  const base: Record<string, number> = {
    Top: -Math.PI / 2,
    TopRight: -Math.PI / 4,
    Right: 0,
    BottomRight: Math.PI / 4,
    Bottom: Math.PI / 2,
    BottomLeft: (3 * Math.PI) / 4,
    Left: Math.PI,
    TopLeft: -(3 * Math.PI) / 4,
  };
  return base[dir] ?? 0;
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