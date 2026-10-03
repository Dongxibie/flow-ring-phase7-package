import { useState } from 'react';
import { useBridge } from '../../bridge/useBridge';
import { useFlowStore } from '../../store/flowStore';

// v20：暗色编辑排版面板。导出/导入的桥接逻辑原样保留。
export function FlowCodePage(): JSX.Element {
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
        <div className="ph">导出</div>
        <div className="frow">
          <label className="flab">
            档案
            <select value={exportProfileId} onChange={(e) => setExportProfileId(e.target.value)}>
              {profiles.map((p: import('../../store/flowStore').ProfileSummary) => (
                <option key={p.id} value={p.id}>{p.name}</option>
              ))}
              {profiles.length === 0 && <option value="default">默认</option>}
            </select>
          </label>
          <label className="flab chk">
            <input
              type="checkbox"
              checked={encrypt}
              onChange={(e) => setEncrypt(e.target.checked)}
            />
            启用 AES-256-GCM 加密
          </label>
        </div>
        <button
          type="button"
          className="fbtn"
          onClick={() => {
            void bridge.exportFlowCode({ profileId: exportProfileId, encrypt }).then((code: string) => {
              setExportedCode(code);
              setStatus({ kind: 'success', message: 'Flow Code 已生成（占位实现，Phase 7 接通真实编码）' });
            }).catch((e: unknown) => {
              setStatus({ kind: 'error', message: e instanceof Error ? e.message : String(e) });
            });
          }}
        >
          生成 Flow Code
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
              复制
            </button>
          </div>
        )}
      </div>

      <div className="panel">
        <div className="ph">导入</div>
        <textarea
          className="codebox"
          placeholder="粘贴 Flow Code"
          value={importCode}
          onChange={(e) => setImportCode(e.target.value)}
        />
        <input
          className="tinput w"
          type="password"
          placeholder="口令（如果加密）"
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
                  setStatus({ kind: 'success', message: '导入成功（占位实现）' });
                } else {
                  setStatus({ kind: 'error', message: r.error ?? '未知错误' });
                }
              });
            }}
          >
            预览并应用
          </button>
        </div>
      </div>
    </div>
  );
}
