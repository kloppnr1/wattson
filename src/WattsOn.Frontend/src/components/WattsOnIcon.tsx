interface WattsOnIconProps {
  size?: number;
}

export default function WattsOnIcon({ size = 24 }: WattsOnIconProps) {
  const s = size;
  const pad = s * 0.125;  // 4/32
  const cell = s * 0.34375; // 11/32
  const gap = s * 0.40625;  // 13/32 (offset of second cell = 17/32 * size)
  const r = s * 0.0625;     // 2/32
  const sw = s * 0.047;     // ~1.5/32

  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      viewBox={`0 0 ${s} ${s}`}
      width={s}
      height={s}
    >
      {/* Top-left */}
      <rect x={pad} y={pad} width={cell} height={cell} rx={r}
        stroke="white" strokeWidth={sw} fill="none" />
      {/* Top-right */}
      <rect x={pad + gap} y={pad} width={cell} height={cell} rx={r}
        stroke="white" strokeWidth={sw} fill="none" />
      {/* Bottom-left */}
      <rect x={pad} y={pad + gap} width={cell} height={cell} rx={r}
        stroke="white" strokeWidth={sw} fill="none" />
      {/* Bottom-right (filled) */}
      <rect x={pad + gap} y={pad + gap} width={cell} height={cell} rx={r}
        fill="rgba(255,255,255,0.3)" stroke="white" strokeWidth={sw} />
    </svg>
  );
}
