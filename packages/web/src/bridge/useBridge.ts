import { useCallback, useEffect, useMemo, useRef } from 'react';
import type {
  ProfileListEntry,
  StudioSavePayload,
  FlowCodeExportPayload,
  FlowCodeImportPayload,
} from '../protocol/messages';

declare global {
  interface Window {
    chrome?: {
      webview?: {
        postMessage(json: string): void;
        addEventListener(event: 'message', handler: (e: { data: string }) => void): void;
      };
    };
  }
}

export type SaveStudioResult = { ok: boolean; error?: string };
export type ImportResult = { ok: boolean; error?: string };
export type LoadStudioResult = { profileJson: string; ringGraphJson: string; actionLibrary: string[] };

export interface BridgeApi {
  isInWebView2: boolean;
  listProfiles(): Promise<ProfileListEntry[]>;
  loadStudio(profileId: string): Promise<LoadStudioResult>;
  saveStudio(payload: StudioSavePayload): Promise<SaveStudioResult>;
  exportFlowCode(payload: { profileId: string; encrypt: boolean }): Promise<string>;
  importFlowCode(payload: { code: string; passphrase: string | null }): Promise<ImportResult>;
  onWebMessage(handler: (json: string) => void): () => void;
}

function isWebView2(): boolean {
  // v19 修复：真实 API 是 window.chrome.webview（没有 "2"）
  return typeof window !== 'undefined' && window.chrome?.webview !== undefined;
}

export function useBridge(): BridgeApi {
  const inWv2 = isWebView2();
  const handlersRef = useRef<Set<(json: string) => void>>(new Set());

  useEffect(() => {
    if (!inWv2 || !window.chrome?.webview) {
      return;
    }
    const wv2 = window.chrome.webview;
    const onMsg = (e: { data: string }) => {
      handlersRef.current.forEach((h: (data: string) => void) => h(e.data));
    };
    wv2.addEventListener('message', onMsg);
    return () => {
      handlersRef.current.clear();
    };
  }, [inWv2]);

  const send = useCallback((msg: unknown) => {
    const json = JSON.stringify(msg);
    if (inWv2 && window.chrome?.webview) {
      window.chrome.webview.postMessage(json);
    } else {
      console.log('[useBridge:stub]', json);
    }
  }, [inWv2]);

  const listProfiles = useCallback(async (): Promise<ProfileListEntry[]> => {
    const sample: ProfileListEntry[] = [
      { id: 'default', name: '默认 Profile', isDefault: true },
      { id: 'developer', name: '开发者', isDefault: false },
    ];
    if (!inWv2) {
      return sample;
    }
    send({ type: 'PROFILE_LIST', v: 1, payload: null });
    return sample;
  }, [inWv2, send]);

  const loadStudio = useCallback(async (profileId: string): Promise<LoadStudioResult> => {
    const emptyProfile = JSON.stringify({
      metadata: {
        id: profileId,
        name: profileId,
        version: '1.0.0',
        schemaVersion: '1.0',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        checksum: '0'.repeat(64),
      },
      ringGraph: { rootId: 'root', nodes: {} },
      actionRefs: ['key-ctrl-shift-t', 'system-screenshot'],
      contextRules: [],
    });
    const emptyRing = JSON.stringify({
      rootId: 'root',
      nodes: {
        root: { id: 'root', profileId, slots: {} },
      },
    });
    return {
      profileJson: emptyProfile,
      ringGraphJson: emptyRing,
      actionLibrary: ['key-ctrl-shift-t', 'system-screenshot'],
    };
  }, []);

  const saveStudio = useCallback(async (payload: StudioSavePayload): Promise<SaveStudioResult> => {
    send({ type: 'STUDIO_SAVE', v: 1, payload });
    return { ok: true };
  }, [send]);

  const exportFlowCode = useCallback(async (payload: { profileId: string; encrypt: boolean }): Promise<string> => {
    const msg: { type: string; v: number; payload: FlowCodeExportPayload } = {
      type: 'FLOW_CODE_EXPORT',
      v: 1,
      payload: { profileId: payload.profileId, encrypt: payload.encrypt },
    };
    send(msg);
    return 'MVP-PLACEHOLDER-FLOW-CODE';
  }, [send]);

  const importFlowCode = useCallback(async (payload: { code: string; passphrase: string | null }): Promise<ImportResult> => {
    const msg: { type: string; v: number; payload: FlowCodeImportPayload } = {
      type: 'FLOW_CODE_IMPORT',
      v: 1,
      payload: { code: payload.code, passphrase: payload.passphrase },
    };
    send(msg);
    return { ok: false, error: 'MVP 阶段未接 host 真实解码' };
  }, [send]);

  const onWebMessage = useCallback((handler: (json: string) => void) => {
    handlersRef.current.add(handler);
    return () => {
      handlersRef.current.delete(handler);
    };
  }, []);

  // v19 白屏根因修复：useBridge() 每次渲染都返回新的对象字面量 →
  // 消费方 useEffect 依赖 [bridge] 时每次渲染都判定依赖变化 → effect 重跑 →
  // listProfiles() 返回新数组 → setProfiles → 再渲染 → 无限循环（约 1.2 万次/秒）。
  // 微任务自续队的循环饿死渲染器主线程 → 永远走不到绘制帧 → 白屏。
  // useMemo 让 bridge 引用跨渲染稳定，循环从根上断掉。
  return useMemo(() => ({
    isInWebView2: inWv2,
    listProfiles,
    loadStudio,
    saveStudio,
    exportFlowCode,
    importFlowCode,
    onWebMessage,
  }), [inWv2, listProfiles, loadStudio, saveStudio, exportFlowCode, importFlowCode, onWebMessage]);
}