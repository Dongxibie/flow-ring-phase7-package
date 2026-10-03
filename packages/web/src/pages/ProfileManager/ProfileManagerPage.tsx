import { useEffect, useState } from 'react';
import { useBridge } from '../../bridge/useBridge';
import { useFlowStore, type ProfileSummary } from '../../store/flowStore';

// v20：暗色编辑排版索引行（编号 + 细线 + 橄榄"使用中"标签）。
// v19 白屏修复口诀仍然有效：effect 依赖只收稳定引用，不收整个 bridge 对象。
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
    return () => {
      cancelled = true;
    };
  }, [bridge.listProfiles, setProfiles]);

  return (
    <div className="stack">
      {error !== null && <p className="err">{error}</p>}
      {profiles.map((p: ProfileSummary, i: number) => (
        <div className="erow" key={p.id}>
          <span className="idx">{String(i + 1).padStart(2, '0')}</span>
          <span className="name">{p.name}</span>
          <span className="pid">{p.id}</span>
          <span className="sp" />
          {p.id === activeProfileId && <span className="tag">使用中</span>}
          <span className="acts">
            {p.id !== activeProfileId && (
              <>
                <b onClick={() => setActiveProfile(p.id)}>设为当前</b>
                {' · '}
              </>
            )}
            编辑 · 复制 · 删除
          </span>
        </div>
      ))}
      <div className="newbtn-wrap">
        <span className="bk tl" />
        <span className="bk tr" />
        <span className="bk bl" />
        <span className="bk br" />
        <div className="newbtn">＋ 新建档案</div>
      </div>
    </div>
  );
}
