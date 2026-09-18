import React, { useState, useMemo } from 'react';
import { ChevronUp, ChevronDown } from 'lucide-react';

export interface Column<T> {
  key?: keyof T | string;
  accessor?: keyof T | string;
  header: string;
  render?: (row: T) => React.ReactNode;
  sortable?: boolean;
  className?: string;
}

interface DataTableProps<T> {
  title?: string;
  columns: Column<T>[];
  data: T[];
  loading?: boolean;
  actions?: React.ReactNode;
  rowKey?: (row: T) => string;
  keyExtractor?: (row: T) => string;
  pageSize?: number;
  emptyMessage?: string;
  emptyText?: string;
  onRowClick?: (row: T) => void;
}

export default function DataTable<T>({
  title, columns, data, loading, actions, rowKey, keyExtractor, pageSize = 20, emptyMessage, emptyText, onRowClick,
}: DataTableProps<T>) {
  const [search, setSearch] = useState('');
  const [sortKey, setSortKey] = useState<string | null>(null);
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>('asc');
  const [page, setPage] = useState(1);

  const getColKey = (col: Column<T>, idx: number): string => (col.key ?? col.accessor ?? String(idx)) as string;
  const getRowKey = (row: T, i: number): string =>
    rowKey ? rowKey(row) : keyExtractor ? keyExtractor(row) : ((row as any)?.id ?? String(i));
  const emptyMsg = emptyMessage ?? emptyText ?? 'Sin datos';

  const filtered = useMemo(() => {
    if (!search) return data;
    return data.filter(row =>
      JSON.stringify(row).toLowerCase().includes(search.toLowerCase())
    );
  }, [data, search]);

  const sorted = useMemo(() => {
    if (!sortKey) return filtered;
    return [...filtered].sort((a, b) => {
      const av = (a as Record<string, unknown>)[sortKey];
      const bv = (b as Record<string, unknown>)[sortKey];
      const cmp = String(av ?? '').localeCompare(String(bv ?? ''), undefined, { numeric: true });
      return sortDir === 'asc' ? cmp : -cmp;
    });
  }, [filtered, sortKey, sortDir]);

  const totalPages = Math.max(1, Math.ceil(sorted.length / pageSize));
  const paged = sorted.slice((page - 1) * pageSize, page * pageSize);

  const handleSort = (key: string) => {
    if (sortKey === key) setSortDir(d => d === 'asc' ? 'desc' : 'asc');
    else { setSortKey(key); setSortDir('asc'); }
  };

  const getCellValue = (row: T, col: Column<T>): React.ReactNode => {
    if (col.render) return col.render(row);
    const k = col.key ?? col.accessor;
    const val = k ? (row as Record<string, unknown>)[k as string] : undefined;
    return val != null ? String(val) : '—';
  };

  return (
    <div className="table-container">
      <div className="table-toolbar">
        <span className="table-title">{title ?? ''}</span>
        <div className="table-actions">
          <div className="search-input">
            <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><circle cx="11" cy="11" r="8"/><path d="m21 21-4.35-4.35"/></svg>
            <input
              placeholder="Buscar..."
              value={search}
              onChange={e => { setSearch(e.target.value); setPage(1); }}
            />
          </div>
          {actions}
        </div>
      </div>

      <div style={{ overflowX: 'auto' }}>
        <table>
          <thead>
            <tr>
              {columns.map((col, idx) => {
                const k = getColKey(col, idx);
                return (
                  <th
                    key={k}
                    onClick={() => col.sortable !== false && handleSort(k)}
                    className={col.className}
                  >
                    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}>
                      {col.header}
                      {col.sortable !== false && sortKey === k && (
                        sortDir === 'asc' ? <ChevronUp size={11} /> : <ChevronDown size={11} />
                      )}
                    </span>
                  </th>
                );
              })}
            </tr>
          </thead>
          <tbody>
            {loading ? (
              Array.from({ length: 5 }).map((_, i) => (
                <tr key={i}>
                  {columns.map((col, idx) => (
                    <td key={getColKey(col, idx)}>
                      <div className="skeleton" style={{ height: 14, width: '80%', borderRadius: 4 }} />
                    </td>
                  ))}
                </tr>
              ))
            ) : paged.length === 0 ? (
              <tr>
                <td colSpan={columns.length}>
                  <div className="empty-state" style={{ padding: 40 }}>
                    <span style={{ color: 'var(--text-muted)', fontSize: 13 }}>{emptyMsg}</span>
                  </div>
                </td>
              </tr>
            ) : paged.map((row, rIdx) => (
              <tr
                key={getRowKey(row, rIdx)}
                onClick={() => onRowClick?.(row)}
                style={onRowClick ? { cursor: 'pointer' } : undefined}
              >
                {columns.map((col, cIdx) => (
                  <td key={getColKey(col, cIdx)} className={col.className}>
                    {getCellValue(row, col)}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="table-footer">
        <span>{filtered.length} registro{filtered.length !== 1 ? 's' : ''}</span>
        {totalPages > 1 && (
          <div style={{ display: 'flex', gap: 4, alignItems: 'center' }}>
            <button className="btn btn-secondary btn-sm" onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page === 1}>←</button>
            <span style={{ fontSize: 12, color: 'var(--text-secondary)' }}>Página {page} de {totalPages}</span>
            <button className="btn btn-secondary btn-sm" onClick={() => setPage(p => Math.min(totalPages, p + 1))} disabled={page === totalPages}>→</button>
          </div>
        )}
      </div>
    </div>
  );
}
