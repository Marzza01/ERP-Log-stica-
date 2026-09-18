import React from 'react';
import type { EstadoOrden } from '../../types';

type BadgeVariant = EstadoOrden | 'Conforme' | 'Discrepante' | 'Pendiente3Way' | 'Procesada' | 'Fallida' | 'Emitida' | 'Recibida' | 'Cerrada' | string;

const variantMap: Record<string, string> = {
  Pendiente:    'badge-pendiente',
  Aprobada:     'badge-aprobada',
  EnProceso:    'badge-enproceso',
  Enviada:      'badge-enviada',
  Entregada:    'badge-entregada',
  Cancelada:    'badge-cancelada',
  Conforme:     'badge-conforme',
  Discrepante:  'badge-discrepante',
  Procesada:    'badge-entregada',
  Fallida:      'badge-cancelada',
  Emitida:      'badge-aprobada',
  Recibida:     'badge-enproceso',
  Cerrada:      'badge-entregada',
};

interface StatusBadgeProps {
  status: BadgeVariant;
  dot?: boolean;
}

export default function StatusBadge({ status, dot = true }: StatusBadgeProps) {
  const cls = variantMap[status] ?? 'badge-default';
  return (
    <span className={`badge ${cls}`}>
      {dot && <span style={{ width: 5, height: 5, borderRadius: '50%', background: 'currentColor', display: 'inline-block', flexShrink: 0 }} />}
      {status}
    </span>
  );
}
