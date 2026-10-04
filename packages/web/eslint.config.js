// ESLint 9 flat config（取代旧 .eslintrc.cjs）：eslint 9 起不再读取 eslintrc 格式。
// 规则集与旧配置一一对应，仅做 flat config 迁移所需的等价改写。
// 依赖全部来自本包已有 devDependencies：typescript-eslint / eslint-plugin-react-hooks /
// eslint-plugin-react-refresh；未引入 @eslint/js（工作区未安装，tseslint.configs.recommended 已够）。
import tseslint from 'typescript-eslint';
import reactHooks from 'eslint-plugin-react-hooks';
import reactRefresh from 'eslint-plugin-react-refresh';

// 旧配置的 env: { browser: true }（不依赖 globals 包，手写常用浏览器全局）。
const browserGlobals = {
  window: 'readonly',
  document: 'readonly',
  location: 'readonly',
  navigator: 'readonly',
  history: 'readonly',
  console: 'readonly',
  localStorage: 'readonly',
  sessionStorage: 'readonly',
  setTimeout: 'readonly',
  clearTimeout: 'readonly',
  setInterval: 'readonly',
  clearInterval: 'readonly',
  requestAnimationFrame: 'readonly',
  cancelAnimationFrame: 'readonly',
  queueMicrotask: 'readonly',
  fetch: 'readonly',
  crypto: 'readonly',
  performance: 'readonly',
  self: 'readonly',
  globalThis: 'readonly',
  structuredClone: 'readonly',
  addEventListener: 'readonly',
  removeEventListener: 'readonly',
  postMessage: 'readonly',
  Event: 'readonly',
  CustomEvent: 'readonly',
  MessageEvent: 'readonly',
  KeyboardEvent: 'readonly',
  MouseEvent: 'readonly',
  ErrorEvent: 'readonly',
  PromiseRejectionEvent: 'readonly',
  HTMLElement: 'readonly',
  HTMLDivElement: 'readonly',
  Element: 'readonly',
  Node: 'readonly',
  AbortController: 'readonly',
  AbortSignal: 'readonly',
  URL: 'readonly',
  URLSearchParams: 'readonly',
  Blob: 'readonly',
  File: 'readonly',
  FileReader: 'readonly',
  FormData: 'readonly',
  Headers: 'readonly',
  Request: 'readonly',
  Response: 'readonly',
  TextEncoder: 'readonly',
  TextDecoder: 'readonly',
  WebSocket: 'readonly',
  Image: 'readonly',
};

export default tseslint.config(
  { ignores: ['dist/', 'node_modules/'] },
  {
    files: ['**/*.{ts,tsx}'],
    extends: [...tseslint.configs.recommended],
    languageOptions: {
      ecmaVersion: 2022,
      sourceType: 'module',
      globals: browserGlobals,
    },
    plugins: {
      'react-hooks': reactHooks,
      'react-refresh': reactRefresh,
    },
    rules: {
      // TS 自己负责未定义标识符检查（tsconfig strict + DOM lib），
      // 打开 core no-undef 会对 TS 类型/全局产生误报，故显式关闭。
      'no-undef': 'off',
      'react-hooks/rules-of-hooks': 'error',
      'react-hooks/exhaustive-deps': 'error',
      'react-refresh/only-export-components': ['warn', { allowConstantExport: true }],
      'no-restricted-imports': [
        'error',
        {
          paths: [
            { name: 'fs', message: 'UI 禁止直访 fs，必须走 useBridge()。' },
            { name: 'path', message: 'UI 禁止直访 path，必须走 useBridge()。' },
            { name: 'child_process', message: 'UI 禁止直访 child_process。' },
          ],
        },
      ],
    },
  },
);
