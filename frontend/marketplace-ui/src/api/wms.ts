/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import { apiClient } from './client';
import type {
  ResumenBodega,
  StockTransferenciaWms,
  TransferenciaInventarioResult,
  UbicacionAlmacen,
} from '../types';

export const wmsApi = {
  getMapaBodega: () =>
    apiClient.get<ResumenBodega>('/api/wms/mapa'),

  listarUbicaciones: (params?: { pasillo?: string; soloDisponibles?: boolean }) => {
    const qs = new URLSearchParams();
    if (params?.pasillo) qs.set('pasillo', params.pasillo);
    if (params?.soloDisponibles !== undefined) qs.set('soloDisponibles', String(params.soloDisponibles));
    return apiClient.get<UbicacionAlmacen[]>(`/api/wms/ubicaciones?${qs}`);
  },

  listarStocks: (params?: { ubicacionId?: string; sku?: string }) => {
    const qs = new URLSearchParams();
    if (params?.ubicacionId) qs.set('ubicacionId', params.ubicacionId);
    if (params?.sku) qs.set('sku', params.sku);
    const suffix = qs.toString() ? `?${qs.toString()}` : '';
    return apiClient.get<StockTransferenciaWms[]>(`/api/wms/stocks${suffix}`);
  },

  transferir: (input: {
    stockFisicoId: string;
    nuevaUbicacionId: string;
    cantidad: number;
    operacionId: string;
    observaciones?: string;
  }) =>
    apiClient.post<TransferenciaInventarioResult>('/api/wms/transferir', input),

  // Compatibilidad con integraciones antiguas. La UI ya no utiliza este método.
  reubicar: (stockFisicoId: string, nuevaUbicacionId: string) =>
    apiClient.post<{ mensaje: string }>('/api/wms/reubicar', { stockFisicoId, nuevaUbicacionId }),
};
