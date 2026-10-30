/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 *
 * Gráfico horizontal de "Valor vs Meta" con bandas semafóricas (Verde / Amarillo / Rojo)
 */

import React from 'react';
import {
  BarChart,
  Bar,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
  ReferenceArea,
  ReferenceLine,
} from 'recharts';
import type { ThresholdConfig } from '../../../constants/kpiDetails';
import { usePrefersReducedMotion } from '../../../hooks/usePrefersReducedMotion';

export interface ZoneBarChartProps {
  valor: number;
  meta: number;
  unidad: string;
  sentido: 'mayor' | 'menor';
  umbrales: ThresholdConfig[];
  color: string;
  titulo?: string;
}

interface ParsedZone {
  colorKey: 'Verde' | 'Amarillo' | 'Rojo';
  colorToken: string;
  x1: number;
  x2: number;
}

export const ZoneBarChart: React.FC<ZoneBarChartProps> = ({
  valor,
  meta,
  unidad,
  sentido,
  color,
  titulo,
}) => {
  const prefersReducedMotion = usePrefersReducedMotion();

  // Valores limpios no negativos
  const safeValor = Math.max(0, Number.isFinite(valor) ? valor : 0);
  const safeMeta = Math.max(0, Number.isFinite(meta) ? meta : 0);

  // Dominio dinámico según especificación:
  // Dominio del eje: máx(valor, meta, umbral más alto) × 1.25; si la unidad es %, tope 100.
  let domainMax = 100;
  if (unidad === '%') {
    domainMax = 100;
  } else {
    const highestBenchmark = Math.max(safeValor, safeMeta, 72, 4);
    domainMax = Math.ceil(highestBenchmark * 1.25);
  }

  // Definición de las 3 zonas semafóricas según la unidad y el sentido del KPI
  const zones: ParsedZone[] = [];
  if (sentido === 'mayor') {
    if (unidad === '%') {
      // Casos Fill Rate (85, 95), OTIF (85, 95), 3-Way Match (90, 98), Margen (8, 9.5)
      // Determinamos el corte verde y amarillo según la meta o umbrales
      let yellowMin = 85;
      let greenMin = 95;
      if (safeMeta >= 98) {
        yellowMin = 90;
        greenMin = 98;
      } else if (safeMeta <= 15) {
        yellowMin = 8;
        greenMin = 9.5;
        domainMax = Math.max(15, Math.ceil(Math.max(safeValor, safeMeta) * 1.25));
      }

      zones.push({ colorKey: 'Rojo', colorToken: 'var(--red)', x1: 0, x2: yellowMin });
      zones.push({ colorKey: 'Amarillo', colorToken: 'var(--yellow)', x1: yellowMin, x2: greenMin });
      zones.push({ colorKey: 'Verde', colorToken: 'var(--green)', x1: greenMin, x2: domainMax });
    } else {
      // Rotación de inventario (meta: 4, Rojo < 2, Amarillo 2 - 3.99, Verde >= 4)
      zones.push({ colorKey: 'Rojo', colorToken: 'var(--red)', x1: 0, x2: 2 });
      zones.push({ colorKey: 'Amarillo', colorToken: 'var(--yellow)', x1: 2, x2: 4 });
      zones.push({ colorKey: 'Verde', colorToken: 'var(--green)', x1: 4, x2: domainMax });
    }
  } else {
    // sentido === 'menor' (Invertido: Verde es el valor más bajo, Rojo el más alto)
    if (unidad === 'horas') {
      // Tiempo Ciclo Pedido (meta 48: Verde <= 48, Amarillo 48.1 - 72, Rojo > 72)
      zones.push({ colorKey: 'Verde', colorToken: 'var(--green)', x1: 0, x2: 48 });
      zones.push({ colorKey: 'Amarillo', colorToken: 'var(--yellow)', x1: 48, x2: 72 });
      zones.push({ colorKey: 'Rojo', colorToken: 'var(--red)', x1: 72, x2: domainMax });
    } else if (unidad === '%') {
      if (safeMeta <= 3.5) {
        // Tasa Devolución RMA (meta 3%: Verde <= 3, Amarillo 3.01 - 7, Rojo > 7)
        domainMax = Math.max(15, Math.ceil(Math.max(safeValor, safeMeta, 10) * 1.25));
        zones.push({ colorKey: 'Verde', colorToken: 'var(--green)', x1: 0, x2: 3 });
        zones.push({ colorKey: 'Amarillo', colorToken: 'var(--yellow)', x1: 3, x2: 7 });
        zones.push({ colorKey: 'Rojo', colorToken: 'var(--red)', x1: 7, x2: domainMax });
      } else {
        // Tasa Cancelación (meta 5%: Verde <= 5, Amarillo 5.01 - 10, Rojo > 10)
        domainMax = Math.max(20, Math.ceil(Math.max(safeValor, safeMeta, 12) * 1.25));
        zones.push({ colorKey: 'Verde', colorToken: 'var(--green)', x1: 0, x2: 5 });
        zones.push({ colorKey: 'Amarillo', colorToken: 'var(--yellow)', x1: 5, x2: 10 });
        zones.push({ colorKey: 'Rojo', colorToken: 'var(--red)', x1: 10, x2: domainMax });
      }
    }
  }

  const chartData = [
    {
      name: titulo || 'KPI',
      valor: safeValor,
    },
  ];

  return (
    <div className="kpi-chart-card">
      <div className="kpi-chart-header">
        <span className="kpi-chart-caption">BARRAS DE RENDIMIENTO VS META Y RANGOS</span>
        <span className="kpi-chart-meta-badge">Meta: {safeMeta} {unidad}</span>
      </div>

      <div className="kpi-chart-canvas" style={{ width: '100%', height: 120 }}>
        <ResponsiveContainer width="100%" height="100%" minWidth={0} minHeight={100}>
          <BarChart
            layout="vertical"
            data={chartData}
            margin={{ top: 12, right: 28, left: 8, bottom: 8 }}
          >
            <CartesianGrid stroke="var(--border)" strokeDasharray="3 3" horizontal={false} />
            <XAxis
              type="number"
              domain={[0, domainMax]}
              tick={{ fontSize: 11, fill: 'var(--text-muted)' }}
              stroke="var(--border)"
              unit={unidad === '%' ? '%' : ''}
            />
            <YAxis
              type="category"
              dataKey="name"
              hide={true}
            />
            <Tooltip
              cursor={{ fill: 'rgba(99, 91, 255, 0.04)' }}
              contentStyle={{
                background: 'var(--bg-surface)',
                border: '1px solid var(--border)',
                borderRadius: 8,
                fontSize: 11,
                boxShadow: 'var(--shadow-md)',
              }}
              formatter={(val: any) => [
                `${Number(val).toLocaleString('es-SV', { maximumFractionDigits: 1 })} ${unidad}`,
                'Valor actual',
              ]}
              labelFormatter={() => titulo || 'Indicador'}
            />

            {/* Franjas semafóricas de fondo */}
            {zones.map((zone, idx) => (
              <ReferenceArea
                key={`zone-${idx}`}
                x1={zone.x1}
                x2={zone.x2}
                fill={zone.colorToken}
                fillOpacity={0.14}
                stroke={zone.colorToken}
                strokeOpacity={0.25}
                strokeWidth={1}
                ifOverflow="extendDomain"
              />
            ))}

            {/* Línea vertical de meta */}
            <ReferenceLine
              x={safeMeta}
              stroke="var(--text-primary)"
              strokeWidth={2}
              strokeDasharray="4 2"
              ifOverflow="extendDomain"
              label={{
                value: `Meta: ${safeMeta}${unidad === '%' ? '%' : ` ${unidad}`}`,
                position: 'insideTopRight',
                fill: 'var(--text-primary)',
                fontSize: 10,
                fontWeight: 700,
                offset: 6,
              }}
            />

            {/* Barra de valor actual */}
            <Bar
              dataKey="valor"
              fill={color || 'var(--accent)'}
              radius={[0, 4, 4, 0]}
              barSize={20}
              isAnimationActive={!prefersReducedMotion}
            />
          </BarChart>
        </ResponsiveContainer>
      </div>

      {/* Leyenda semafórica inferior */}
      <div className="kpi-chart-legend">
        {zones.map(z => (
          <div key={z.colorKey} className="kpi-chart-legend-item">
            <span
              className="kpi-chart-legend-dot"
              style={{ background: z.colorToken }}
            />
            <span className="kpi-chart-legend-label">Rango {z.colorKey}</span>
            <span className="kpi-chart-legend-val">
              ({z.x1} - {z.x2}{unidad === '%' ? '%' : ` ${unidad}`})
            </span>
          </div>
        ))}
      </div>
    </div>
  );
};

export default ZoneBarChart;

