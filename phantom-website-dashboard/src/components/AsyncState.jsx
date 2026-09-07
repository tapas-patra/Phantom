export function DashboardSkeleton({ label = "Dashboard", cards = 8 }) {
  return (
    <main className="page" aria-busy="true" aria-label={`Loading ${label.toLowerCase()}`}>
      <span className="sr-only" role="status">Loading {label.toLowerCase()}.</span>
      <section className="dashboard-shell dashboard-skeleton" aria-hidden="true">
        <aside className="glass-panel dashboard-rail skeleton-rail">
          <SkeletonLine width="58%" />
          <SkeletonLine width="84%" />
          <div className="skeleton-nav">
            {Array.from({ length: 6 }, (_, index) => <SkeletonLine key={index} width={`${72 + (index % 3) * 8}%`} />)}
          </div>
        </aside>
        <section className="dashboard-main dashboard-grid">
          <div className="glass-panel dashboard-hero skeleton-hero"><SkeletonLine width="28%" /><SkeletonLine width="82%" /><SkeletonLine width="64%" /></div>
          {Array.from({ length: cards }, (_, index) => <div className="glass-panel skeleton-card" key={index}><SkeletonLine width={`${54 + (index % 3) * 13}%`} /><SkeletonLine width="38%" /></div>)}
        </section>
      </section>
    </main>
  );
}

export function SectionSkeleton({ rows = 4, label = "Loading section" }) {
  return (
    <div className="glass-panel section-skeleton table-span-full" aria-busy="true" aria-label={label}>
      <span className="sr-only" role="status">{label}.</span>
      {Array.from({ length: rows }, (_, index) => <SkeletonLine key={index} width={`${88 - (index % 3) * 14}%`} />)}
    </div>
  );
}

export function RetryNotice({ message, onRetry, retryLabel = "Try again", className = "" }) {
  if (!message) return null;
  return (
    <div className={`status-message status-error recovery-notice ${className}`.trim()} role="alert">
      <span>{message}</span>
      {onRetry ? <button className="button button-secondary button-compact" type="button" onClick={onRetry}>{retryLabel}</button> : null}
    </div>
  );
}

function SkeletonLine({ width }) {
  return <span className="skeleton-line" style={{ width }} />;
}
