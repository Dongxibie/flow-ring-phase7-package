// v20.1：中英双语切换。模块级单例 + useSyncExternalStore 订阅，
// 选择持久化到 localStorage（flowring.lang），默认中文。
import { useSyncExternalStore } from 'react';

export type Lang = 'zh' | 'en';

const KEY = 'flowring.lang';
let current: Lang = 'zh';
try {
  if (localStorage.getItem(KEY) === 'en') {
    current = 'en';
  }
} catch {
  // localStorage 不可用时保持默认
}

const subs = new Set<() => void>();

export function getLang(): Lang {
  return current;
}

export function setLang(l: Lang): void {
  current = l;
  try {
    localStorage.setItem(KEY, l);
  } catch {
    // 忽略
  }
  subs.forEach((f) => f());
}

function subscribe(cb: () => void): () => void {
  subs.add(cb);
  return () => {
    subs.delete(cb);
  };
}

export function useLang(): Lang {
  return useSyncExternalStore(subscribe, getLang);
}

/** 取当前语言的文案；组件必须同时调用 useLang() 以获得切换时的重渲染。 */
export function t(zh: string, en: string): string {
  return current === 'zh' ? zh : en;
}
