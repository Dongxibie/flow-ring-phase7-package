import { useEffect, useState } from 'react';
import { useBridge } from '../../bridge/useBridge';
import { useFlowStore, type ProfileSummary } from '../../store/flowStore';

export function ProfileManagerPage(): JSX.Element {
  const bridge = useBridge();
  const profiles = useFlowStore((s: import('../../store/flowStore').FlowState) => s.profiles);
  const setProfiles = useFlowStore((s: import('../../store/flowStore').FlowState) => s.setProfiles);
  const setActiveProfile = useFlowStore((s: import('../../store/flowStore').FlowState) => s.setActiveProfile);
  const activeProfileId = useFlowStore((s: import('../../store/flowStore').FlowState) => s.activeProfileId);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    void bridge.listProfiles().then((list) => {
      if (cancelled) {
        return;
      }
      setProfiles(list);
    }).catch((e: unknown) => {
      setError(e instanceof Error ? e.message : String(e));
    });
    // v19 白屏根因修复：依赖从整个 bridge 对象收敛到 listProfiles（useCallback 稳定引用）。
    // 之前 [bridge, setProfiles] 里 bridge 每次 render 都是新对象 → effect 每渲染重跑 →
    // listProfiles 新 sample 数组 → setProfiles → 再渲染 → 微任务级死循环饿死渲染帧（白屏）。
    return () => {
      cancelled = true;
    };
  }, [bridge.listProfiles, setProfiles]);

  return (
    <section style={{ padding: '16px' }}>
      <header style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <h2>Profile 管理</h2>
        <button type="button">+ 新建 Profile</button>
      </header>

      {error !== null && <p style={{ color: 'crimson' }}>{error}</p>}

      <ul style={{ listStyle: 'none', padding: 0, display: 'grid', gap: '12px', gridTemplateColumns: 'repeat(auto-fill, minmax(260px, 1fr))' }}>
        {profiles.map((p: ProfileSummary) => (
          <li
            key={p.id}
            style={{
              border: '1px solid var(--fr-border, #dadbe1)',
              borderRadius: '12px',
              padding: '16px',
              background: p.id === activeProfileId ? 'rgba(0, 180, 0, 0.08)' : 'transparent',
            }}
          >
            <strong>{p.name}</strong>
            {p.isDefault && <span style={{ marginLeft: '8px', fontSize: '12px' }}>默认</span>}
            <p style={{ fontSize: '12px', opacity: 0.7 }}>{p.id}</p>
            <div style={{ display: 'flex', gap: '8px', marginTop: '8px' }}>
              <button
                type="button"
                onClick={() => setActiveProfile(p.id)}
                disabled={p.id === activeProfileId}
              >
                设为当前
              </button>
              <button type="button">编辑</button>
              <button type="button">复制</button>
              <button type="button" disabled={p.isDefault}>删除</button>
            </div>
          </li>
        ))}
      </ul>
    </section>
  );
}