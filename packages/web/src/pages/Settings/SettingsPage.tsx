import { useFlowStore } from '../../store/flowStore';
import { useLang, t } from '../../i18n';

const TRIGGER_KEY_OPTIONS = [
  { value: 'MouseSideButton', zh: '鼠标侧键长按', en: 'Mouse side button (hold)' },
  { value: 'MiddleButton', zh: '中键长按', en: 'Middle button (hold)' },
  { value: 'RightButtonLongPress', zh: '右键长按', en: 'Right button (long press)' },
  { value: 'HotKey', zh: '自定义热键', en: 'Custom hotkey' },
];

// v20.1：设置面板 + 中英双语 + 环外观（透明度/大小，实时生效并持久化）。
export function SettingsPage(): JSX.Element {
  const lang = useLang();
  const settings = useFlowStore((s: import('../../store/flowStore').FlowState) => s.settings);
  const update = useFlowStore((s: import('../../store/flowStore').FlowState) => s.updateSettings);

  return (
    <div className="stack">
      <div className="panel">
        <div className="ph">{t('环外观', 'RING APPEARANCE')}</div>
        <div className="rngrow">
          <span className="rl">{t('透明度', 'Opacity')}</span>
          <input
            type="range"
            className="rng"
            min={25}
            max={100}
            value={Math.round(settings.ringOpacity * 100)}
            onChange={(e) => update({ ringOpacity: Number(e.target.value) / 100 })}
          />
          <span className="rv">{Math.round(settings.ringOpacity * 100)}%</span>
        </div>
        <div className="rngrow">
          <span className="rl">{t('大小', 'Size')}</span>
          <input
            type="range"
            className="rng"
            min={320}
            max={640}
            step={20}
            value={settings.ringSizePx}
            onChange={(e) => update({ ringSizePx: Number(e.target.value) })}
          />
          <span className="rv">{settings.ringSizePx}px</span>
        </div>
        <p className="note">{t('对环工作室与右键唤起的圆环同时生效，自动保存。', 'Applies to both Ring Studio and the right-click ring. Saved automatically.')}</p>
      </div>

      <div className="panel">
        <div className="ph">{t('触发键', 'TRIGGER KEY')}</div>
        <label className="flab">
          {t('默认触发', 'Default trigger')}
          <select
            value={settings.triggerKey}
            onChange={(e) => update({ triggerKey: e.target.value })}
          >
            {TRIGGER_KEY_OPTIONS.map((o) => (
              <option key={o.value} value={o.value}>{lang === 'zh' ? o.zh : o.en}</option>
            ))}
          </select>
        </label>
        <p className="note">
          {t('MVP 默认仅 MouseSideButton，其他项在 v1.1 启用。', 'MVP ships MouseSideButton only; the rest activate in v1.1.')}
        </p>
      </div>

      <div className="panel">
        <div className="ph">{t('死区半径', 'DEAD ZONE')}</div>
        <label className="flab">
          {t('半径（像素）', 'Radius (px)')}
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
        <div className="ph">{t('动画时长', 'ANIMATION')}</div>
        <label className="flab">
          {t('持续（毫秒）', 'Duration (ms)')}
          <input
            className="tinput"
            type="number"
            min={0}
            max={1000}
            value={settings.animationDurationMs}
            onChange={(e) => update({ animationDurationMs: Math.max(0, Math.min(1000, Number(e.target.value))) })}
          />
        </label>
        <p className="note">
          {t('MVP 不接自定义主题和动画时长，本字段仅写值不生效（v1.1 启用）。', 'MVP stores this value without applying it (activates in v1.1).')}
        </p>
      </div>

      <div className="panel">
        <div className="ph">{t('主题', 'THEME')}</div>
        <div className="radios">
          <label>
            <input
              type="radio"
              name="theme"
              checked={settings.theme === 'auto'}
              onChange={() => update({ theme: 'auto' })}
            />
            {t('跟随系统', 'System')}
          </label>
          <label>
            <input
              type="radio"
              name="theme"
              checked={settings.theme === 'light'}
              onChange={() => update({ theme: 'light' })}
            />
            {t('浅色', 'Light')}
          </label>
          <label>
            <input
              type="radio"
              name="theme"
              checked={settings.theme === 'dark'}
              onChange={() => update({ theme: 'dark' })}
            />
            {t('深色', 'Dark')}
          </label>
        </div>
      </div>

      <div className="newbtn-wrap">
        <span className="bk tl" />
        <span className="bk tr" />
        <span className="bk bl" />
        <span className="bk br" />
        <button type="button" className="newbtn">{t('保存设置', 'Save Settings')}</button>
      </div>
    </div>
  );
}
