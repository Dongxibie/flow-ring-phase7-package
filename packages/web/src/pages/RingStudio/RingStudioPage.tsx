import { useEffect, useMemo, useState } from 'react';
import { useBridge } from '../../bridge/useBridge';
import { SegmentedRing, EIGHT_DIRECTIONS, type SegSlot } from '../../components/SegmentedRing';
import { useFlowStore } from '../../store/flowStore';
import { useLang, t } from '../../i18n';
import { actionName, actionKey, loadCustomActions, saveCustomActions, newCustomAction, type CustomAction } from '../../actions';
import { loadSlots, saveSlots } from '../../slotStore';
import { loadActiveProfileId } from '../../profileStore';

// v20.1：环工作室——整环切割 + 功能标注进扇区（不再挂环外）。
// 拖拽指派、属性面板、保存链路原样保留；环尺寸/透明度跟随设置实时生效。
interface SlotViewModel extends SegSlot {
  direction: string;
}

export function RingStudioPage(): JSX.Element {
  const lang = useLang();
  const bridge = useBridge();
  const settings = useFlowStore((s: import('../../store/flowStore').FlowState) => s.settings);
  const [profileId] = useState<string>(loadActiveProfileId());
  const [slots, setSlots] = useState<Record<string, SlotViewModel>>({});
  const [actionLibrary, setActionLibrary] = useState<string[]>([]);
  const [selectedSlot, setSelectedSlot] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  // v22：自定义动作（用户自己命名 + 定义功能）
  const [customs, setCustoms] = useState<CustomAction[]>(() => loadCustomActions());
  const [showForm, setShowForm] = useState(false);
  const [cName, setCName] = useState('');
  const [cKind, setCKind] = useState<'key' | 'app'>('key');
  const [cParam, setCParam] = useState('');

  useEffect(() => {
    let cancelled = false;
    void bridge.loadStudio(profileId).then(({ ringGraphJson, actionLibrary: lib }: { ringGraphJson: string; actionLibrary: string[] }) => {
      if (cancelled) {
        return;
      }
      try {
        const ring = JSON.parse(ringGraphJson) as { nodes: Record<string, { slots: Record<string, SegSlot> }> };
        const root = ring.nodes.root;
        const mapped: Record<string, SlotViewModel> = {};
        for (const dir of EIGHT_DIRECTIONS) {
          const s = root.slots[dir];
          mapped[dir] = s
            ? { direction: dir, kind: s.kind, actionRef: s.actionRef, childRingId: s.childRingId }
            : { direction: dir, kind: 'empty' };
        }
        // v21：本地已保存的指派优先（loadSlots），叠加 host 图谱兜底
        const saved = loadSlots();
        for (const dir of EIGHT_DIRECTIONS) {
          if (saved[dir]?.kind === 'action') {
            mapped[dir] = { direction: dir, ...saved[dir] };
          }
        }
        setSlots(mapped);
        // v22：动作库 = 内置 + 自定义（自定义以 custom:<id> 引用）
        const customRefs = loadCustomActions().map((c) => `custom:${c.id}`);
        setActionLibrary([...lib, ...customRefs]);
      } catch (e: unknown) {
        setError(e instanceof Error ? e.message : t('Ring 图解析失败', 'Failed to parse ring graph'));
      }
    });
    return () => {
      cancelled = true;
    };
    // v19 白屏修复口诀：依赖收敛到稳定方法引用
  }, [bridge.loadStudio, profileId]);

  // v21：指派即持久化（覆盖层 / 右键唤起读同一份）
  useEffect(() => {
    if (Object.keys(slots).length > 0) {
      saveSlots(slots, profileId);
    }
  }, [slots, profileId]);

  const actionSlotCount = useMemo(
    () => Object.values(slots).filter((s) => s.kind === 'action').length,
    [slots],
  );

  return (
    <div
      className="studio-inner"
      onDragOver={(e) => e.preventDefault()}
      onDrop={(e) => {
        e.preventDefault();
        const actionRef = e.dataTransfer.getData('text/plain');
        // v21.1 修复：拖拽面 = 整页，方向以环心（SegmentedRing 的 ringwrap）为基准
        const ringCenter = document.querySelector('.studio .ringwrap')?.getBoundingClientRect();
        const dx = ringCenter ? e.clientX - (ringCenter.left + ringCenter.width / 2) : 0;
        const dy = ringCenter ? e.clientY - (ringCenter.top + ringCenter.height / 2) : 0;
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
      {error !== null && <p className="err">{error}</p>}

      <>
        <SegmentedRing
          size={Math.min(settings.ringSizePx, Math.round(window.innerHeight * 0.62))}
          opacity={settings.ringOpacity}
          bgColor="#171614"
          slots={slots}
          selected={selectedSlot}
          onSelect={setSelectedSlot}
          nameOf={(r) => actionName(r, lang)}
          keyOf={actionKey}
          emptyLabel={t('空槽位', 'Empty')}
          childLabel={(id) => (lang === 'zh' ? `子环 · ${id ?? '?'}` : `Sub-ring · ${id ?? '?'}`)}
        />
      </>

      <aside className="slotpanel">
        <div className="panel">
          <div className="ph">{t('属性面板', 'PROPERTIES')} · {t('已绑定', 'BOUND')} {actionSlotCount}/8</div>
          {selectedSlot === null ? (
            <p className="hint-p">{t('点击环上的扇区以编辑该方向。', 'Click a sector on the ring to edit it.')}</p>
          ) : (
            <div>
              <p className="cur">{t('方向', 'Direction')}：<strong>{dirCn(selectedSlot, lang)}（{selectedSlot}）</strong></p>
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
                <option value="">{t('（空）', '(empty)')}</option>
                {actionLibrary.map((a) => (
                  <option key={a} value={a}>{actionName(a, lang)} · {a}</option>
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
                  {t('保存', 'Save')}
                </button>
                <button type="button" className="fbtn ghost" onClick={() => setSelectedSlot(null)}>
                  {t('取消', 'Cancel')}
                </button>
              </div>
            </div>
          )}
        </div>

        <div className="panel">
          <div className="ph second">
            {t('动作库 · 拖入环上指派', 'ACTIONS · DRAG ONTO THE RING')}
            <button type="button" className="fbtn mini" onClick={() => setShowForm(!showForm)}>
              ＋ {t('自定义', 'Custom')}
            </button>
          </div>
          {showForm && (
            <div className="cform">
              <input
                className="tinput w"
                placeholder={t('名称（如：打开前端）', 'Name (e.g. Open Frontend)')}
                value={cName}
                onChange={(e) => setCName(e.target.value)}
              />
              <div className="frow">
                <label className="flab chk">
                  <input type="radio" checked={cKind === 'key'} onChange={() => setCKind('key')} />
                  {t('快捷键', 'Hotkey')}
                </label>
                <label className="flab chk">
                  <input type="radio" checked={cKind === 'app'} onChange={() => setCKind('app')} />
                  {t('启动应用 / 网址', 'Launch app / URL')}
                </label>
              </div>
              <input
                className="tinput w"
                placeholder={cKind === 'key' ? t('如 Ctrl+Alt+T', 'e.g. Ctrl+Alt+T') : t('如 notepad.exe 或 https://...', 'e.g. notepad.exe or https://...')}
                value={cParam}
                onChange={(e) => setCParam(e.target.value)}
              />
              <div className="btnrow">
                <button
                  type="button"
                  className="fbtn"
                  disabled={cName.trim() === '' || cParam.trim() === ''}
                  onClick={() => {
                    const def = newCustomAction(cName.trim(), cKind, cParam);
                    const next = [...loadCustomActions(), def];
                    saveCustomActions(next);
                    setCustoms(next);
                    setActionLibrary((prev) => [...prev, `custom:${def.id}`]);
                    setCName('');
                    setCParam('');
                    setShowForm(false);
                  }}
                >
                  {t('创建', 'Create')}
                </button>
                <button type="button" className="fbtn ghost" onClick={() => setShowForm(false)}>{t('取消', 'Cancel')}</button>
              </div>
            </div>
          )}
          <ul className="lib">
            {actionLibrary.map((a) => {
              const isCustom = a.startsWith('custom:');
              const removable = isCustom && customs.some((c) => `custom:${c.id}` === a);
              return (
                <li
                  key={a}
                  draggable
                  onDragStart={(e) => e.dataTransfer.setData('text/plain', a)}
                >
                  {actionName(a, lang)}
                  {!isCustom && <span className="acode">{a}</span>}
                  {removable && (
                    <span
                      className="adel"
                      title={t('删除此自定义动作', 'Delete this custom action')}
                      onClick={() => {
                        const next = customs.filter((c) => `custom:${c.id}` !== a);
                        saveCustomActions(next);
                        setCustoms(next);
                        setActionLibrary((prev) => prev.filter((x) => x !== a));
                      }}
                    >
                      ×
                    </span>
                  )}
                </li>
              );
            })}
          </ul>
        </div>
      </aside>
    </div>
  );
}

function dirCn(dir: string, lang: 'zh' | 'en'): string {
  const m: Record<string, [string, string]> = {
    Top: ['上', 'Top'], TopRight: ['右上', 'Top-Right'], Right: ['右', 'Right'],
    BottomRight: ['右下', 'Bottom-Right'], Bottom: ['下', 'Bottom'],
    BottomLeft: ['左下', 'Bottom-Left'], Left: ['左', 'Left'], TopLeft: ['左上', 'Top-Left'],
  };
  const pair = m[dir] ?? [dir, dir];
  return lang === 'zh' ? pair[0] : pair[1];
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
