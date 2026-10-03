import { useState } from 'react';

// v20.1：整环切割构件（环工作室与右键覆盖层共用）。
// 尺寸与玻璃不透明度全部参数化（设置页滑杆实时生效）；
// 功能标注（动作名 + 快捷键）直接放进扇区内部，随扇区旋转、下半球自动翻转保持可读。
export const EIGHT_DIRECTIONS = ['Top', 'TopRight', 'Right', 'BottomRight', 'Bottom', 'BottomLeft', 'Left', 'TopLeft'];

// 楔形多边形：以"右"扇区为基准（屏幕坐标 y 向下），45° 扇区 ±21°
export const WEDGE: Record<string, string> = {
  Top: 'polygon(50% 50%, 32.1% 3.3%, 37.0% 1.7%, 42.2% 0.6%, 47.4% 0.1%, 52.6% 0.1%, 57.8% 0.6%, 63.0% 1.7%, 67.9% 3.3%)',
  TopRight: 'polygon(50% 50%, 70.4% 4.3%, 75.0% 6.7%, 79.4% 9.6%, 83.4% 12.9%, 87.1% 16.6%, 90.4% 20.6%, 93.3% 25.0%, 95.7% 29.6%)',
  Right: 'polygon(50% 50%, 96.7% 32.1%, 98.3% 37.0%, 99.4% 42.2%, 99.9% 47.4%, 99.9% 52.6%, 99.4% 57.8%, 98.3% 63.0%, 96.7% 67.9%)',
  BottomRight: 'polygon(50% 50%, 95.7% 70.4%, 93.3% 75.0%, 90.4% 79.4%, 87.1% 83.4%, 83.4% 87.1%, 79.4% 90.4%, 75.0% 93.3%, 70.4% 95.7%)',
  Bottom: 'polygon(50% 50%, 67.9% 96.7%, 63.0% 98.3%, 57.8% 99.4%, 52.6% 99.9%, 47.4% 99.9%, 42.2% 99.4%, 37.0% 98.3%, 32.1% 96.7%)',
  BottomLeft: 'polygon(50% 50%, 29.6% 95.7%, 25.0% 93.3%, 20.6% 90.4%, 16.6% 87.1%, 12.9% 83.4%, 9.6% 79.4%, 6.7% 75.0%, 4.3% 70.4%)',
  Left: 'polygon(50% 50%, 3.3% 67.9%, 1.7% 63.0%, 0.6% 57.8%, 0.1% 52.6%, 0.1% 47.4%, 0.6% 42.2%, 1.7% 37.0%, 3.3% 32.1%)',
  TopLeft: 'polygon(50% 50%, 4.3% 29.6%, 6.7% 25.0%, 9.6% 20.6%, 12.9% 16.6%, 16.6% 12.9%, 20.6% 9.6%, 25.0% 6.7%, 29.6% 4.3%)',
};

const ANGLE: Record<string, number> = {
  Top: -90, TopRight: -45, Right: 0, BottomRight: 45,
  Bottom: 90, BottomLeft: 135, Left: 180, TopLeft: -135,
};

export interface SegSlot {
  kind: 'empty' | 'action' | 'childRing';
  actionRef?: string;
  childRingId?: string;
}

interface SegmentedRingProps {
  size: number; // 外直径 px
  opacity: number; // 0.25~1，玻璃不透明度倍率
  bgColor: string; // 切缝颜色（跟随所处界面的底色）
  slots: Record<string, SegSlot>;
  selected: string | null;
  onSelect?: (dir: string) => void;
  onHover?: (dir: string | null) => void;
  nameOf: (ref: string) => string;
  keyOf: (ref: string) => string;
  emptyLabel: string;
  childLabel: (id?: string) => string;
  core?: boolean;
}

const ANNULUS = (ri: number): Record<string, string> => ({
  WebkitMaskImage: `radial-gradient(farthest-side, transparent ${ri - 1}px, #000 ${ri}px)`,
  maskImage: `radial-gradient(farthest-side, transparent ${ri - 1}px, #000 ${ri}px)`,
});

export function SegmentedRing(p: SegmentedRingProps): JSX.Element {
  const r = p.size / 2;
  const ri = Math.round(r * 0.56); // 内圈半径（与 500/140 同比例）
  const lr = (r + ri) / 2; // 标注所在的中带半径
  const bandLen = r - ri; // 环带厚度（辐条/空槽位中线长度）
  const glassAlpha = (0.14 * p.opacity).toFixed(3);
  const mask = ANNULUS(ri);
  const [hover, setHover] = useState<string | null>(null);
  const lit = (dir: string): boolean => p.selected === dir || hover === dir;

  return (
    <div className="ringwrap">
      <div className="ring" style={{ left: -r, top: -r, width: p.size, height: p.size }}>
        <div className="rg rg-glass" style={{ background: `rgba(255,255,255,${glassAlpha})`, ...mask }} />
        {p.selected !== null && (
          <div className="rg rg-active" style={{ clipPath: WEDGE[p.selected], ...mask }} />
        )}
        {/* v22：径向竖线切 8 块——8 条边界辐条始终可见 */}
        {[-22.5, 22.5, 67.5, 112.5, 157.5, 202.5, 247.5, 292.5].map((deg) => {
          const rad = (deg * Math.PI) / 180;
          const mx = Math.cos(rad) * lr;
          const my = Math.sin(rad) * lr;
          return (
            <div
              key={'spoke-' + deg}
              className="spoke"
              style={{
                // .ring 自身偏移在 (-r,-r)，子元素坐标需以环左上角为原点：+r
                left: r + mx - 1,
                top: r + my - bandLen / 2,
                height: bandLen,
                transform: `rotate(${deg}deg)`,
              }}
            />
          );
        })}
        <div className="rg-rim" />
        <div className="rg-rim-in" style={{ inset: ri }} />
        {p.onSelect !== undefined &&
          EIGHT_DIRECTIONS.map((dir) => (
            <div
              key={'hit-' + dir}
              className="wedge"
              style={{ clipPath: WEDGE[dir] }}
              onClick={() => p.onSelect!(dir)}
              onMouseEnter={() => {
                setHover(dir);
                p.onHover?.(dir);
              }}
              onMouseLeave={() => {
                setHover(null);
                p.onHover?.(null);
              }}
            />
          ))}
      </div>

      {EIGHT_DIRECTIONS.map((dir) => {
        const a = ANGLE[dir];
        const rad = (a * Math.PI) / 180;
        const x = Math.cos(rad) * lr;
        const y = Math.sin(rad) * lr;
        const slot = p.slots[dir];
        const info = slot?.kind === 'action' && slot.actionRef ? { name: p.nameOf(slot.actionRef), key: p.keyOf(slot.actionRef) } : null;
        const on = lit(dir);
        return (
          <div
            key={dir}
            className={'seg' + (on ? ' on' : '')}
            style={{ left: x, top: y }}
          >
            {on && (
              <div className="selframe">
                <i className="sd tl" /><i className="sd tm" /><i className="sd tr" />
                <i className="sd lm" /><i className="sd rm" />
                <i className="sd bl" /><i className="sd bm" /><i className="sd br" />
              </div>
            )}
            {!info && slot?.kind !== 'childRing' && (
              <div
                className="spoke mid"
                style={{
                  left: r + Math.cos(rad) * lr - 0.5,
                  top: r + Math.sin(rad) * lr - bandLen / 2,
                  height: bandLen,
                  transform: `rotate(${a}deg)`,
                }}
              />
            )}
            <span className={'snm' + (info ? '' : ' empty')}>
              {info
                ? info.name
                : slot?.kind === 'childRing'
                  ? p.childLabel(slot.childRingId)
                  : p.emptyLabel}
            </span>
            {info && info.key !== '' && <span className="skey">{info.key}</span>}
          </div>
        );
      })}

      {p.core !== false && (
        <div className="core"><i /></div>
      )}
    </div>
  );
}
