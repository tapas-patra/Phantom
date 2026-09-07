import { useId, useMemo } from "react";

export function TimelineStep({ index, title, body }) {
  return <article className="timeline-step"><span>{index}</span><h3>{title}</h3><p>{body}</p></article>;
}

export function MetricCard({ label, value, tone = "default" }) {
  return <article className={`glass-panel metric-card tone-${tone}`}><span>{label}</span><strong>{value}</strong></article>;
}

export function MetricDefinition({ title, detail }) {
  return <article className="definition-card"><h3>{title}</h3><p>{detail}</p></article>;
}

export function InfoRow({ label, value }) {
  return <div className="info-row"><span>{label}</span><strong>{value}</strong></div>;
}

export function MiniBarList({ items }) {
  const maxValue = Math.max(...items.map((item) => item.value), 1);
  return (
    <div className="mini-bars">
      {items.map((item) => <div className="mini-bar-row" key={item.label}>
        <div className="mini-bar-meta"><span>{item.label}</span><strong>{item.value}</strong></div>
        <div className="mini-bar-track" role="img" aria-label={`${item.label}: ${item.value}`}><div className="mini-bar-fill" style={{ width: `${(item.value / maxValue) * 100}%` }} /></div>
      </div>)}
    </div>
  );
}

export function SimpleSparkline({ values, label = "Recent trend" }) {
  const gradientId = useId();
  const points = useMemo(() => {
    const usable = values.length > 0 ? values : [0, 0, 0];
    const max = Math.max(...usable, 1);
    return usable.map((value, index) => {
      const x = (index / Math.max(usable.length - 1, 1)) * 100;
      const y = 100 - (Number(value || 0) / max) * 100;
      return `${x},${y}`;
    }).join(" ");
  }, [values]);

  return (
    <svg className="sparkline" viewBox="0 0 100 100" preserveAspectRatio="none" role="img" aria-label={label}>
      <polyline fill="none" stroke={`url(#${gradientId})`} strokeWidth="4" points={points} />
      <defs><linearGradient id={gradientId} x1="0" y1="0" x2="1" y2="0"><stop offset="0%" stopColor="#4cc9f0" /><stop offset="100%" stopColor="#34d399" /></linearGradient></defs>
    </svg>
  );
}
