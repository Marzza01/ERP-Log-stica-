/** Exportación P3 de KPIs observados. Nunca convertir datos inexistentes en cero comercial. */
import type { DashboardKpis } from '../types';

const escapeCsv = (value: unknown): string => {
  const raw = String(value ?? '');
  // Evita que Excel/LibreOffice ejecuten fórmulas provenientes de texto dinámico.
  const safe = /^[\s]*[=+@-]/.test(raw) ? "'" + raw : raw;
  return '"' + safe.replace(/"/g, '""') + '"';
};

export function crearReporteKpisCsv(data: DashboardKpis): string {
  if (!data || !data.periodo || !data.resumenEjecutivo || !Array.isArray(data.kpis)) {
    throw new Error('No hay KPIs verificables para exportar.');
  }
  const lines: unknown[][] = [
    ['LOGIMKT - Informe de KPIs (datos del servidor)'],
    ['Desde', data.periodo.desde],
    ['Hasta', data.periodo.hasta],
    ['Días', data.periodo.diasPeriodo],
    ['Generado por API', data.generadoEn],
    ['Indicador', 'Valor', 'Unidad', 'Meta', 'Semáforo', 'Datos disponibles'],
  ];
  for (const k of data.kpis) {
    const available = k.tieneDatos !== false && Number.isFinite(k.valor);
    lines.push([k.nombre, available ? k.valor : 'Sin datos', k.unidad,
      Number.isFinite(k.meta) ? k.meta : 'Sin meta', available ? k.semaforo : 'Sin datos',
      available ? 'Sí' : 'No']);
  }
  return '\uFEFF' + lines.map(values => values.map(escapeCsv).join(',')).join('\r\n') + '\r\n';
}
