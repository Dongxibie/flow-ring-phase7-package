import { useEffect, useState } from 'react';
import { useFlowStore, type ProfileSummary } from '../../store/flowStore';
import { useLang, t } from '../../i18n';
import {
  loadProfiles,
  saveProfiles,
  loadActiveProfileId,
  saveActiveProfileId,
  newProfileId,
} from '../../profileStore';
import { loadSlots, saveSlots, removeSlots } from '../../slotStore';

// v22：档案管理真实现——新建/重命名/复制/删除/设为当前全部落 localStorage，
// 每个档案对应一套独立的环指派（slotStore 按档案 id 分键）。
// bridge.listProfiles 仅保留作 host 遥测，本地档案库是唯一事实源。
export function ProfileManagerPage(): JSX.Element {
  useLang(); // 订阅语言切换（文案经 t() 读取当前语言）
  const profiles = useFlowStore((s: import('../../store/flowStore').FlowState) => s.profiles);
  const setProfiles = useFlowStore((s: import('../../store/flowStore').FlowState) => s.setProfiles);
  const setActiveProfile = useFlowStore((s: import('../../store/flowStore').FlowState) => s.setActiveProfile);
  const activeProfileId = useFlowStore((s: import('../../store/flowStore').FlowState) => s.activeProfileId);
  const [error, setError] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [newName, setNewName] = useState('');
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editName, setEditName] = useState('');

  useEffect(() => {
    const local = loadProfiles();
    setProfiles(local);
    const active = loadActiveProfileId();
    setActiveProfile(local.some((p) => p.id === active) ? active : local[0].id);
  }, [setProfiles, setActiveProfile]);

  const persist = (list: ProfileSummary[]): void => {
    setProfiles(list);
    saveProfiles(list);
  };

  const activate = (id: string): void => {
    setActiveProfile(id);
    saveActiveProfileId(id);
  };

  const confirmCreate = (): void => {
    const name = newName.trim();
    if (name.length === 0) {
      return;
    }
    const id = newProfileId();
    persist([...profiles, { id, name, isDefault: false }]);
    activate(id);
    setNewName('');
    setCreating(false);
  };

  const confirmRename = (): void => {
    const name = editName.trim();
    if (editingId === null || name.length === 0) {
      return;
    }
    persist(profiles.map((p) => (p.id === editingId ? { ...p, name } : p)));
    setEditingId(null);
  };

  const copyProfile = (p: ProfileSummary): void => {
    const id = newProfileId();
    // v23.1：复制档案时深拷贝槽位（loadSlots 逐方向重建，避免引用共享）
    const src = loadSlots(p.id);
    saveSlots(src, id);
    persist([...profiles, { id, name: `${p.name}·${t('副本', 'copy')}`, isDefault: false }]);
  };

  const deleteProfile = (p: ProfileSummary): void => {
    if (p.id === activeProfileId && profiles.length <= 1) {
      setError(t('至少保留一个档案', 'Keep at least one profile'));
      return;
    }
    removeSlots(p.id);
    const next = profiles.filter((x) => x.id !== p.id);
    persist(next);
    if (p.id === activeProfileId) {
      activate(next[0].id);
    }
  };

  return (
    <div className="stack">
      {error !== null && <p className="err">{error}</p>}
      {profiles.map((p: ProfileSummary, i: number) => (
        <div className="erow" key={p.id}>
          <span className="idx">{String(i + 1).padStart(2, '0')}</span>
          {editingId === p.id ? (
            <span className="erow-edit">
              <input
                className="tinput"
                value={editName}
                autoFocus
                onChange={(e) => setEditName(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') confirmRename();
                  if (e.key === 'Escape') setEditingId(null);
                }}
              />
              <button type="button" className="fbtn" onClick={confirmRename}>{t('确定', 'OK')}</button>
              <button type="button" className="fbtn ghost" onClick={() => setEditingId(null)}>{t('取消', 'Cancel')}</button>
            </span>
          ) : (
            <>
              <span className="name">{p.name}</span>
              <span className="pid">{p.id}</span>
              <span className="sp" />
              {p.id === activeProfileId && <span className="tag">{t('使用中', 'ACTIVE')}</span>}
              <span className="acts">
                {p.id !== activeProfileId && (
                  <>
                    <b onClick={() => activate(p.id)}>{t('设为当前', 'Set Active')}</b>
                    {' · '}
                  </>
                )}
                <b onClick={() => { setEditingId(p.id); setEditName(p.name); }}>{t('编辑', 'Rename')}</b>
                {' · '}
                <b onClick={() => copyProfile(p)}>{t('复制', 'Copy')}</b>
                {' · '}
                <b onClick={() => deleteProfile(p)}>{t('删除', 'Delete')}</b>
              </span>
            </>
          )}
        </div>
      ))}

      {creating ? (
        <div className="erow">
          <span className="idx">{String(profiles.length + 1).padStart(2, '0')}</span>
          <span className="erow-edit">
            <input
              className="tinput"
              placeholder={t('档案名称', 'Profile name')}
              value={newName}
              autoFocus
              onChange={(e) => setNewName(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') confirmCreate();
                if (e.key === 'Escape') setCreating(false);
              }}
            />
            <button type="button" className="fbtn" onClick={confirmCreate}>{t('确定', 'OK')}</button>
            <button type="button" className="fbtn ghost" onClick={() => setCreating(false)}>{t('取消', 'Cancel')}</button>
          </span>
        </div>
      ) : (
        <div className="newbtn-wrap">
          <span className="bk tl" />
          <span className="bk tr" />
          <span className="bk bl" />
          <span className="bk br" />
          <div className="newbtn" onClick={() => setCreating(true)}>＋ {t('新建档案', 'New Profile')}</div>
        </div>
      )}
    </div>
  );
}
