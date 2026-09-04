export function DataTable({ title, columns, rows, emptyLabel = "No records found.", footer = null, scrollClassName = "", loading = false }) {
  return (
    <div className="glass-panel table-panel table-span-full" aria-busy={loading || undefined}>
      <div className="table-header">
        <p className="eyebrow">{title}</p>
        {loading ? <span className="status-pill" role="status">Updating…</span> : null}
      </div>
      <TableScroll className={scrollClassName}>
        <table>
          <caption className="sr-only">{title}</caption>
          <thead>
            <tr>{columns.map((column) => <th key={column} scope="col">{column}</th>)}</tr>
          </thead>
          <tbody>
            {!rows || rows.length === 0 ? (
              <tr><td colSpan={columns.length}>{loading ? "Loading records…" : emptyLabel}</td></tr>
            ) : rows.map((row, index) => (
              <tr key={`${title}-${index}`}>
                {row.map((cell, cellIndex) => <td key={`${title}-${index}-${cellIndex}`}>{cell}</td>)}
              </tr>
            ))}
          </tbody>
        </table>
      </TableScroll>
      {footer}
    </div>
  );
}

export function TableScroll({ children, className = "" }) {
  return <div className={`table-scroll ${className}`.trim()}>{children}</div>;
}

export function PaginationBar({ page, hasNextPage, totalCount, onPrevious, onNext, disabled = false }) {
  return (
    <nav className="pagination-bar" aria-label="Table pages">
      <span>{totalCount} total</span>
      <div className="inline-actions">
        <button className="button button-ghost button-compact" type="button" onClick={onPrevious} disabled={disabled || page <= 1}>Previous</button>
        <span className="status-pill" aria-current="page">Page {page}</span>
        <button className="button button-ghost button-compact" type="button" onClick={onNext} disabled={disabled || !hasNextPage}>Next</button>
      </div>
    </nav>
  );
}
