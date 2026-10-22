/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import React from 'react';
import { ChevronRight } from 'lucide-react';
import type { Kpi, SemaforoColor } from '../../types';

interface KpiCardProps {
  kpi: Kpi;
  onClick?: () => void;
}

const semaforoColors: Record<SemaforoColor, string> = {
  Verde: 'var(--green)',
  Amarillo: 'var(--yellow)',
  Rojo: 'var(--red)',
};

export default function KpiCard({ kpi, onClick }: KpiCardProps) {
  const color = semaforoColors[kpi.semaforo];
  const pct = Math.min(100, Math.max(0, kpi.meta > 0 ? (kpi.valor / kpi.meta) * 100 : 0));
  const cls = kpi.semaforo.toLowerCase();

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (onClick && (e.key === 'Enter' || e.key === ' ')) {
      e.preventDefault();
      onClick();
    }
  };

  return (
    <div
      className={`kpi-card ${cls}`}
      title={kpi.descripcion}
      role={onClick ? 'button' : undefined}
      tabIndex={onClick ? 0 : undefined}
      aria-haspopup={onClick ? 'dialog' : undefined}
      onClick={onClick}
      onKeyDown={onClick ? handleKeyDown : undefined}
    >
      <div className="kpi-header">
        <span className="kpi-label" style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
          {kpi.nombre}
          {onClick && <ChevronRight size={11} style={{ color: 'var(--text-muted)', opacity: 0.7 }} />}
        </span>
        <span className={`semaforo ${cls}`}>
          <span style={{ width: 6, height: 6, borderRadius: '50%', background: color, display: 'inline-block' }} />
          {kpi.semaforo}
        </span>
      </div>

      <div style={{ display: 'flex', alignItems: 'baseline', gap: 4 }}>
        <span className="kpi-value">{kpi.valor.toLocaleString('es-SV', { maximumFractionDigits: 1 })}</span>
        <span className="kpi-unit">{kpi.unidad}</span>
      </div>

      <div className="kpi-progress">
        <div
          className="kpi-progress-bar"
          style={{ width: `${pct}%`, background: color }}
        />
      </div>

      <div className="kpi-footer">
        <span className="kpi-meta">Meta: {kpi.meta}{kpi.unidad}</span>
        <span style={{ fontSize: 11, color: kpi.cumpleMeta ? 'var(--green)' : 'var(--red)', fontWeight: 600 }}>
          {kpi.cumpleMeta ? '✓ Cumple' : '✗ No cumple'}
        </span>
      </div>
    </div>
  );
}
