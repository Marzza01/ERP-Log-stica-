/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import { apiClient } from './client';
import type { Liquidacion } from '../types';

export interface GenerarLiquidacionRequest {
  vendedorId: string;
}

export const liquidacionesApi = {
  pendientes: () =>
    apiClient.get<Liquidacion[]>('/api/liquidaciones/pendientes'),

  historial: (vendedorId?: string) =>
    apiClient.get<Liquidacion[]>(`/api/liquidaciones/historial${vendedorId ? `?vendedorId=${vendedorId}` : ''}`),

  generar: (vendedorId: string) =>
    apiClient.post<any>('/api/liquidaciones/generar', { vendedorId }),

  procesarPago: (id: string) =>
    apiClient.post<any>(`/api/liquidaciones/procesar-pago/${id}`),
};
