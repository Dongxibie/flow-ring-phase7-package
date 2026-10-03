import { useState } from 'react';
import { useBridge } from '../../bridge/useBridge';
import { useFlowStore } from '../../store/flowStore';
import { useLang, t } from '../../i18n';

// v20.1：流码面板 + 中英双语。导出/导入的桥接逻辑原样保留。
export function FlowCodePage(): JSX.Element {
  useLang(); // 订阅语言切换（文案经 t() 读取当前语言）
  const bridge = useBridge();
  const profiles = useFlowStore((s: import('../../store/flowStore').FlowState) => s.profiles);
  const [exportProfileId, setExportProfileId] = useState<string>(profiles[0]?.id ?? 'default');
  const [encrypt, setEncrypt] = useState<boolean>(false);
  const [exportedCode, setExportedCode] = useState<string>('');
  const [importCode, setImportCode] = useState<string>('');
  const [importPassphrase, setImportPassphrase] = useState<string>('');
  const [status, setStatus] = useState<{ kind: 'idle' | 'success' | 'error'; message: string }>({ kind: 'idle', message: '' });

  return (
    <div className="stack">
      {status.kind !== 'idle' && (
        <p className={status.kind === 'success' ? 'status-ok' : 'status-err'}>{status.message}</p>
      )}

      <div className="panel">
        <div className="ph">{t('导出', 'EXPORT')}</div>
        <div className="frow">
          <label className="flab">
            {t('档案', 'Profile')}
            <select value={exportProfileId} onChange={(e) => setExportProfileId(e.target.value)}>
              {profiles.map((p: import('../../store/flowStore').ProfileSummary) => (
                <option key={p.id} value={p.id}>{p.name}</option>
              ))}
              {profiles.length === 0 && <option value="default">{t('默认', 'Default')}</option>}
            </select>
          </label>
          <label className="flab chk">
            <input
              type="checkbox"
              checked={encrypt}
              onChange={(e) => setEncrypt(e.target.checked)}
            />
            {t('启用 AES-256-GCM 加密', 'Encrypt with AES-256-GCM')}
          </label>
        </div>
        <button
          type="button"
          className="fbtn"
          onClick={() => {
            void bridge.exportFlowCode({ profileId: exportProfileId, encrypt }).then((code: string) => {
              setExportedCode(code);
              setStatus({
                kind: 'success',
                message: t('Flow Code 已生成（占位实现，Phase 7 接通真实编码）', 'Flow Code generated (placeholder; real codec lands in Phase 7)'),
              });
            }).catch((e: unknown) => {
              setStatus({ kind: 'error', message: e instanceof Error ? e.message : String(e) });
            });
          }}
        >
          {t('生成 Flow Code', 'Generate Flow Code')}
        </button>
        {exportedCode !== '' && (
          <div>
            <textarea className="codebox" readOnly value={exportedCode} />
            <button
              type="button"
              className="fbtn ghost"
              onClick={() => {
                void navigator.clipboard.writeText(exportedCode);
              }}
            >
              {t('复制', 'Copy')}
            </button>
          </div>
        )}
      </div>

      <div className="panel">
        <div className="ph">{t('导入', 'IMPORT')}</div>
        <textarea
          className="codebox"
          placeholder={t('粘贴 Flow Code', 'Paste a Flow Code')}
          value={importCode}
          onChange={(e) => setImportCode(e.target.value)}
        />
        <input
          className="tinput w"
          type="password"
          placeholder={t('口令（如果加密）', 'Passphrase (if encrypted)')}
          value={importPassphrase}
          onChange={(e) => setImportPassphrase(e.target.value)}
        />
        <div className="btnrow">
          <button
            type="button"
            className="fbtn"
            disabled={importCode.trim().length === 0}
            onClick={() => {
              void bridge.importFlowCode({ code: importCode, passphrase: importPassphrase.length === 0 ? null : importPassphrase }).then((r: import('../../bridge/useBridge').ImportResult) => {
                if (r.ok) {
                  setStatus({ kind: 'success', message: t('导入成功（占位实现）', 'Imported (placeholder)') });
                } else {
                  setStatus({ kind: 'error', message: r.error ?? t('未知错误', 'Unknown error') });
                }
              });
            }}
          >
            {t('预览并应用', 'Preview & Apply')}
          </button>
        </div>
      </div>
    </div>
  );
}
