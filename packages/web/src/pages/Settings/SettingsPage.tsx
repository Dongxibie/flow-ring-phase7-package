import { useFlowStore } from '../../store/flowStore';

const TRIGGER_KEY_OPTIONS = [
  { value: 'MouseSideButton', label: '鼠标侧键长按' },
  { value: 'MiddleButton', label: '中键长按' },
  { value: 'RightButtonLongPress', label: '右键长按' },
  { value: 'HotKey', label: '自定义热键' },
];

// v20：暗色编辑排版面板。设置字段与写值逻辑原样保留。
export function SettingsPage(): JSX.Element {
  const settings = useFlowStore((s: import('../../store/flowStore').FlowState) => s.settings);
  const update = useFlowStore((s: import('../../store/flowStore').FlowState) => s.updateSettings);

  return (
    <div className="stack">
      <div className="panel">
        <div className="ph">触发键</div>
        <label className="flab">
          默认触发
          <select
            value={settings.triggerKey}
            onChange={(e) => update({ triggerKey: e.target.value })}
          >
            {TRIGGER_KEY_OPTIONS.map((o) => (
              <option key={o.value} value={o.value}>{o.label}</option>
            ))}
          </select>
        </label>
        <p className="note">MVP 默认仅 MouseSideButton，其他项在 v1.1 启用。</p>
      </div>

      <div className="panel">
        <div className="ph">死区半径</div>
        <label className="flab">
          半径（像素）
          <input
            className="tinput"
            type="number"
            min={10}
            max={120}
            value={settings.deadZoneRadiusPx}
            onChange={(e) => update({ deadZoneRadiusPx: Math.max(10, Math.min(120, Number(e.target.value))) })}
          />
        </label>
      </div>

      <div className="panel">
        <div className="ph">动画时长</div>
        <label className="flab">
          持续（毫秒）
          <input
            className="tinput"
            type="number"
            min={0}
            max={1000}
            value={settings.animationDurationMs}
            onChange={(e) => update({ animationDurationMs: Math.max(0, Math.min(1000, Number(e.target.value))) })}
          />
        </label>
        <p className="note">MVP 不接自定义主题和动画时长，本字段仅写值不生效（v1.1 启用）。</p>
      </div>

      <div className="panel">
        <div className="ph">主题</div>
        <div className="radios">
          <label>
            <input
              type="radio"
              name="theme"
              checked={settings.theme === 'auto'}
              onChange={() => update({ theme: 'auto' })}
            />
            跟随系统
          </label>
          <label>
            <input
              type="radio"
              name="theme"
              checked={settings.theme === 'light'}
              onChange={() => update({ theme: 'light' })}
            />
            浅色
          </label>
          <label>
            <input
              type="radio"
              name="theme"
              checked={settings.theme === 'dark'}
              onChange={() => update({ theme: 'dark' })}
            />
            深色
          </label>
        </div>
      </div>

      <div className="newbtn-wrap">
        <span className="bk tl" />
        <span className="bk tr" />
        <span className="bk bl" />
        <span className="bk br" />
        <button type="button" className="newbtn">保存设置</button>
      </div>
    </div>
  );
}
