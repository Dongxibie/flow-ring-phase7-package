import { useEffect, useState } from 'react';
import { useBridge } from '../../bridge/useBridge';
import { useFlowStore, type ProfileSummary } from '../../store/flowStore';
import { useLang, t } from '../../i18n';

// v20.1：档案索引行 + 中英双语。
// v19 白屏修复口诀仍然有效：effect 依赖只收稳定引用，不收整个 bridge 对象。
export function ProfileManagerPage(): JSX.Element {
  useLang(); // 订阅语言切换（文案经 t() 读取当前语言）
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
          {p.id === activeProfileId && <span className="tag">{t('使用中', 'ACTIVE')}</span>}
          <span className="acts">
            {p.id !== activeProfileId && (
              <>
                <b onClick={() => setActiveProfile(p.id)}>{t('设为当前', 'Set Active')}</b>
                {' · '}
              </>
            )}
            {t('编辑', 'Edit')} · {t('复制', 'Copy')} · {t('删除', 'Delete')}
          </span>
        </div>
      ))}
      <div className="newbtn-wrap">
        <span className="bk tl" />
        <span className="bk tr" />
        <span className="bk bl" />
        <span className="bk br" />
        <div className="newbtn">＋ {t('新建档案', 'New Profile')}</div>
      </div>
    </div>
  );
}
