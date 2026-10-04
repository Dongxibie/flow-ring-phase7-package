import { useState, type CSSProperties } from 'react';

// v20.1：整环切割构件（环工作室与右键覆盖层共用）。
// 尺寸与玻璃不透明度全部参数化（设置页滑杆实时生效）；
// 功能标注（动作名 + 快捷键）直接放进扇区内部，随扇区旋转、下半球自动翻转保持可读。
// 这两个常量数组/对象是模块级只读数据（仅供其他模块 import 复用），文件同时导出组件属预期用法；
// allowConstantExport 只识别字面量初始化，对数组/对象字面量不生效，故按行放行（不关闭整条规则）。
// eslint-disable-next-line react-refresh/only-export-components
export const EIGHT_DIRECTIONS = ['Top', 'TopRight', 'Right', 'BottomRight', 'Bottom', 'BottomLeft', 'Left', 'TopLeft'];

// 楔形多边形：以"右"扇区为基准（屏幕坐标 y 向下），45° 扇区 ±21°
// 同上：只读常量数据，按行放行 only-export-components
// eslint-disable-next-line react-refresh/only-export-components
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

// v25（视觉方案 C · 极简线框 HUD）：细线、刻度、弧光、指针全部改由内联 SVG 绘制
// （viewBox 0 0 100 100、圆心 50,50）。几何数值与 design-demos/ring.html 的 variantC() 等比换算：
// demo 外半径 228 → 这里 47，demo 的像素偏移按比例落到本环上，环尺寸变化时视觉不变形。
const VIEW = 100; // viewBox 边长（环容器为正方形，1 单位 = size/100 px）
const R_OUT = 47; // 外圈半径（viewBox 单位）
const DEMO_R = 228; // demo C 的外半径，用于把 demo 的像素偏移换算成比例
const DEMO_K = R_OUT / DEMO_R; // demo → 本环的等比系数
const TICK_GAP = 8 * DEMO_K; // 分界刻度两端各留的缺口（demo：8px）
const TICK_DEGS = [-22.5, 22.5, 67.5, 112.5, 157.5, 202.5, 247.5, 292.5]; // 8 条扇区分界线
const ARC_HALF = 21; // 弧光半跨角（±21°）
const ARC_W_PX = 5; // 弧光宽度（约 5px，不随环尺寸变粗）
const OLIVE = '#DBDDA1'; // 橄榄（= CSS 变量 --olive）
const LINE_OUT = 'rgba(255,255,255,.30)'; // 外圈细线
const LINE_IN = 'rgba(255,255,255,.18)'; // 内圈细线
const LINE_TICK = 'rgba(255,255,255,.16)'; // 分界刻度

export interface SegSlot {
  kind: 'empty' | 'action' | 'childRing';
  actionRef?: string;
  childRingId?: string;
}

interface SegmentedRingProps {
  size: number; // 外直径 px
  opacity: number; // 0.25~1，整环不透明度倍率（作用于 .ringwrap；设置页滑杆实时生效）
  bgColor: string; // 切缝颜色（v25 起无线框外底色，仅为兼容调用方保留）
  darkGlass?: boolean; // v22.2：弹窗模式下环盘用不透明暗玻璃（v25：同上保留）
  slots: Record<string, SegSlot>;
  selected: string | null;
  hovered?: string | null; // v23：外部高亮方向（手势跟随）
  onSelect?: (dir: string) => void;
  onHover?: (dir: string | null) => void;
  nameOf: (ref: string) => string;
  keyOf: (ref: string) => string;
  emptyLabel: string;
  childLabel: (id?: string) => string;
  core?: boolean;
}

export function SegmentedRing(p: SegmentedRingProps): JSX.Element {
  const r = p.size / 2;
  const ri = Math.round(r * 0.56); // 内圈半径（与 500/140 同比例）
  const lr = (r + ri) / 2; // 标注所在的中带半径
  const [hover, setHover] = useState<string | null>(null);
  const lit = (dir: string): boolean => p.selected === dir || hover === dir || p.hovered === dir;
  // v24：激活方向（手势指向/悬停/选中统一走这一层）——v25 起用于外缘弧光、指针与其余方向压暗
  const fillDir = p.hovered ?? hover ?? p.selected;

  // v25：SVG 换算——u 是 1 CSS px 对应的 viewBox 单位，故细线恒为 1px、弧光恒约 5px
  const u = VIEW / p.size;
  const riU = R_OUT * (ri / r); // 内圈半径（保持 ri/r 比例不变）
  const arcDeg = fillDir !== null ? ANGLE[fillDir] : null; // 弧光/指针所在方向角
  const arcRad = ((arcDeg ?? 0) * Math.PI) / 180;
  const circ = 2 * Math.PI * R_OUT; // 外圈周长
  const arcLen = circ * ((ARC_HALF * 2) / 360); // 弧光长度（跨 42°）
  const arcStart = ((((arcDeg ?? 0) - ARC_HALF) % 360) + 360) % 360; // 弧光起点角（归一到 0~360）
  const arcStartLen = (circ * arcStart) / 360;
  const triR = R_OUT - (ARC_W_PX * u) / 2; // 指针锚点：弧光内缘
  const triX = 50 + Math.cos(arcRad) * triR;
  const triY = 50 + Math.sin(arcRad) * triR;
  // 指针形：顶点朝圆心（局部 -x 为内），-8 / +3 / ±6 px 按 demo 比例换算
  const triD = `M ${-8 * DEMO_K} 0 L ${3 * DEMO_K} ${-6 * DEMO_K} L ${3 * DEMO_K} ${6 * DEMO_K} Z`;
  const coreD = Math.round(r * 0.667); // 中央细环直径（demo C：半径 = R/3）
  // v25 修复：整环不透明度经 CSS 变量作用在 .ringwrap 上（滑入动画 keyframes 终点同取该变量，避免动画结束回弹）
  const ringVars = { '--ring-op': String(p.opacity) } as CSSProperties;

  return (
    <div className="ringwrap" style={ringVars}>
      <div className="ring" style={{ left: -r, top: -r, width: p.size, height: p.size }}>
        {/* v25：细线框——外圈 1px/.30、内圈 1px/.18、8 条分界刻度两端留缺口（不再有玻璃盘与楔形高亮） */}
        <svg className="ring-lines" viewBox="0 0 100 100">
          <circle cx="50" cy="50" r={R_OUT} fill="none" stroke={LINE_OUT} strokeWidth={u} />
          <circle cx="50" cy="50" r={riU} fill="none" stroke={LINE_IN} strokeWidth={u} />
          {TICK_DEGS.map((d) => {
            const a = (d * Math.PI) / 180;
            const c = Math.cos(a);
            const s = Math.sin(a);
            return (
              <line
                key={'tick-' + d}
                x1={50 + c * (riU + TICK_GAP)}
                y1={50 + s * (riU + TICK_GAP)}
                x2={50 + c * (R_OUT - TICK_GAP)}
                y2={50 + s * (R_OUT - TICK_GAP)}
                stroke={LINE_TICK}
                strokeWidth={u}
              />
            );
          })}
        </svg>
        {/* v25：激活方向的外缘弧光（橄榄、圆头、发光由 .ring-glow 的 drop-shadow 提供）+ 指向圆心的指针 */}
        {arcDeg !== null && (
          <svg className="ring-glow" viewBox="0 0 100 100">
            <circle
              cx="50"
              cy="50"
              r={R_OUT}
              fill="none"
              stroke={OLIVE}
              strokeWidth={ARC_W_PX * u}
              strokeLinecap="round"
              strokeDasharray={`${arcLen} ${circ - arcLen}`}
              strokeDashoffset={circ - arcStartLen}
            />
            <path d={triD} fill={OLIVE} transform={`translate(${triX} ${triY}) rotate(${arcDeg ?? 0})`} />
          </svg>
        )}
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
        {/* v24：中心守卫圈——楔形从圆心起算，光标停在环心会误悬停到某个扇区；
            这个透明圆盖住内圈，让"圆心/内圈"既不悬停也不点击（外圈扇区不受影响） */}
        {p.onSelect !== undefined && (
          <div
            className="ring-center-guard"
            style={{ left: r - ri, top: r - ri, width: ri * 2, height: ri * 2 }}
          />
        )}
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
            className={'seg' + (on ? ' on' : '') + (fillDir !== null && !on ? ' dim' : '')}
            style={{ left: x, top: y }}
          >
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

      {/* v25：中央细环（core 为 false 时依旧不渲染） */}
      {p.core !== false && (
        <div className="core" style={{ width: coreD, height: coreD }} />
      )}
    </div>
  );
}
