/**
 * ERP desarrollado por Marcelo Zepeda
 * Todos los derechos reservados © 2026
 */

import { apiClient } from './client';
import type { ResumenBodega, UbicacionAlmacen } from '../types';

export const wmsApi = {
  getMapaBodega: () =>
    apiClient.get<ResumenBodega>('/api/wms/mapa'),

  listarUbicaciones: (params?: { pasillo?: string; soloDisponibles?: boolean }) => {
    const qs = new URLSearchParams();
    if (params?.pasillo) qs.set('pasillo', params.pasillo);
    if (params?.soloDisponibles !== undefined) qs.set('soloDisponibles', String(params.soloDisponibles));
    return apiClient.get<UbicacionAlmacen[]>(`/api/wms/ubicaciones?${qs}`);
  },

  reubicar: (stockFisicoId: string, nuevaUbicacionId: string) =>
    apiClient.post<{ mensaje: string }>('/api/wms/reubicar', { stockFisicoId, nuevaUbicacionId }),
};
