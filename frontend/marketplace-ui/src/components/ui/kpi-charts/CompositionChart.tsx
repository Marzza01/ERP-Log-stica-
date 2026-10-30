/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 *
 * Gráfico de composición (Donut o Barra horizontal apilada) con leyenda personalizada y ResponsiveContainer
 */

import React from 'react';
import {
  PieChart,
  Pie,
  Cell,
  Tooltip,
  ResponsiveContainer,
  BarChart,
  Bar,
  XAxis,
  YAxis,
  CartesianGrid,
} from 'recharts';
import { usePrefersReducedMotion } from '../../../hooks/usePrefersReducedMotion';

export interface CompositionItem {
  nombre: string;
  valor: number;
  color: string;
  badge?: string;
  esMontoDinero?: boolean;
}

export interface CompositionChartProps {
  tipo?: 'donut' | 'stacked-bar';
  datos: CompositionItem[];
  centroTitulo?: string;
  centroSubtitulo?: string;
  textoReferenciaMeta?: string;
  esMontoDinero?: boolean;
}

export const CompositionChart: React.FC<CompositionChartProps> = ({
  tipo = 'donut',
  datos,
  centroTitulo,
  centroSubtitulo,
  textoReferenciaMeta,
  esMontoDinero = false,
}) => {
  const prefersReducedMotion = usePrefersReducedMotion();

  // Filtrar y sanitizar valores no negativos
  const sanitized = datos.map(item => ({
    ...item,
    valor: Math.max(0, Number.isFinite(item.valor) ? item.valor : 0),
  }));

  const total = sanitized.reduce((acc, curr) => acc + curr.valor, 0);

  // Proteger estado vacío (si el total es 0 o no hay datos suficientes)
  if (total <= 0) {
    return (
      <div className="kpi-chart-card">
        <div className="empty-state kpi-chart-empty-state">
          <span className="empty-state-text">Sin datos suficientes para graficar en este período</span>
        </div>
      </div>
    );
  }

  // Formateador de valores
  const formatVal = (num: number, isMoney?: boolean) => {
    if (isMoney || esMontoDinero) {
      return `$${num.toLocaleString('es-SV', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
    }
    return num.toLocaleString('es-SV');
  };

  return (
    <div className="kpi-chart-card">
      {textoReferenciaMeta && (
        <div className="kpi-chart-meta-banner">
          <span>{textoReferenciaMeta}</span>
        </div>
      )}

      {tipo === 'donut' ? (
        <div className="kpi-chart-donut-wrap">
          <div className="kpi-chart-donut-canvas">
            <ResponsiveContainer width="100%" height={170} minWidth={0} minHeight={150}>
              <PieChart margin={{ top: 0, right: 0, bottom: 0, left: 0 }}>
                <Pie
                  data={sanitized}
                  dataKey="valor"
                  nameKey="nombre"
                  cx="50%"
                  cy="50%"
                  innerRadius={46}
                  outerRadius={68}
                  paddingAngle={3}
                  isAnimationActive={!prefersReducedMotion}
                >
                  {sanitized.map((entry, index) => (
                    <Cell key={`cell-${index}`} fill={entry.color} stroke="var(--bg-surface)" strokeWidth={2} />
                  ))}
                </Pie>
                <Tooltip
                  contentStyle={{
                    background: 'var(--bg-surface)',
                    border: '1px solid var(--border)',
                    borderRadius: 8,
                    fontSize: 11,
                    boxShadow: 'var(--shadow-md)',
                  }}
                  formatter={(val: any, name: any) => {
                    const numericVal = Number(val) || 0;
                    const pct = total > 0 ? ((numericVal / total) * 100).toFixed(1) : '0';
                    return [`${formatVal(numericVal)} (${pct}%)`, String(name)];
                  }}
                />
              </PieChart>
            </ResponsiveContainer>

            {/* Texto en el centro del Donut si fue provisto */}
            {centroTitulo && (
              <div className="kpi-chart-donut-center">
                <span className="kpi-chart-donut-center-val">{centroTitulo}</span>
                {centroSubtitulo && (
                  <span className="kpi-chart-donut-center-sub">{centroSubtitulo}</span>
                )}
              </div>
            )}
          </div>

          {/* Leyenda personalizada estructurada */}
          <div className="kpi-chart-donut-legend">
            {sanitized.map((item, idx) => {
              const pct = total > 0 ? ((item.valor / total) * 100).toFixed(1) : '0';
              return (
                <div key={`legend-${idx}`} className="kpi-chart-donut-legend-row">
                  <div className="kpi-chart-donut-legend-info">
                    <span className="kpi-chart-legend-dot" style={{ background: item.color }} />
                    <span className="kpi-chart-legend-label">
                      {item.nombre}
                      {item.badge && <span className="kpi-chart-badge">{item.badge}</span>}
                    </span>
                  </div>
                  <div className="kpi-chart-donut-legend-nums">
                    <span className="kpi-chart-donut-val">{formatVal(item.valor, item.esMontoDinero)}</span>
                    <span className="kpi-chart-donut-pct">({pct}%)</span>
                  </div>
                </div>
              );
            })}
          </div>
        </div>
      ) : (
        /* Barra apilada horizontal (para Comisión u otros desgloses) */
        <div className="kpi-chart-stacked-wrap">
          <div style={{ width: '100%', height: 75 }}>
            <ResponsiveContainer width="100%" height="100%" minWidth={0} minHeight={60}>
              <BarChart
                layout="vertical"
                data={[
                  sanitized.reduce(
                    (acc, item, idx) => ({ ...acc, [`seg_${idx}`]: item.valor }),
                    { name: 'Comisión' }
                  ),
                ]}
                margin={{ top: 8, right: 12, left: 0, bottom: 8 }}
              >
                <CartesianGrid stroke="var(--border)" strokeDasharray="3 3" horizontal={false} />
                <XAxis type="number" hide={true} domain={[0, total]} />
                <YAxis type="category" dataKey="name" hide={true} />
                <Tooltip
                  contentStyle={{
                    background: 'var(--bg-surface)',
                    border: '1px solid var(--border)',
                    borderRadius: 8,
                    fontSize: 11,
                    boxShadow: 'var(--shadow-md)',
                  }}
                  formatter={(val: any, name: any) => {
                    const idx = Number(String(name).replace('seg_', ''));
                    const item = sanitized[idx];
                    const numVal = Number(val) || 0;
                    const pct = total > 0 ? ((numVal / total) * 100).toFixed(1) : '0';
                    return [`${formatVal(numVal, item?.esMontoDinero)} (${pct}%)`, item?.nombre ?? 'Segmento'];
                  }}
                />
                {sanitized.map((item, idx) => (
                  <Bar
                    key={`bar-seg-${idx}`}
                    dataKey={`seg_${idx}`}
                    name={`seg_${idx}`}
                    stackId="a"
                    fill={item.color}
                    isAnimationActive={!prefersReducedMotion}
                    radius={
                      idx === 0 && sanitized.length === 1
                        ? [4, 4, 4, 4]
                        : idx === 0
                        ? [4, 0, 0, 4]
                        : idx === sanitized.length - 1
                        ? [0, 4, 4, 0]
                        : [0, 0, 0, 0]
                    }
                  />
                ))}
              </BarChart>
            </ResponsiveContainer>
          </div>

          {/* Leyenda de la barra apilada */}
          <div className="kpi-chart-stacked-legend">
            {sanitized.map((item, idx) => {
              const pct = total > 0 ? ((item.valor / total) * 100).toFixed(1) : '0';
              return (
                <div key={`stacked-leg-${idx}`} className="kpi-chart-stacked-legend-item">
                  <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                    <span className="kpi-chart-legend-dot" style={{ background: item.color }} />
                    <span className="kpi-chart-legend-label">
                      {item.nombre} {item.badge && <span className="kpi-chart-badge">{item.badge}</span>}
                    </span>
                  </div>
                  <div style={{ display: 'flex', gap: 6, alignItems: 'center' }}>
                    <span className="kpi-chart-donut-val">{formatVal(item.valor, item.esMontoDinero)}</span>
                    <span className="kpi-chart-donut-pct">({pct}%)</span>
                  </div>
                </div>
              );
            })}
          </div>
        </div>
      )}
    </div>
  );
};

export default CompositionChart;

