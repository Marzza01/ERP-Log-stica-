/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import { apiClient } from './client';
import type { ClasificacionRMA, DevolucionRMA } from '../types';

export interface RegistrarRmaRequest {
  ordenId: string;
  productoId: string;
  cantidad: number;
  motivoCliente: string;
}

export interface DictaminarRmaRequest {
  clasificacion: ClasificacionRMA;
  inspector: string;
  dictamenTecnico: string;
  descuentoRemate?: number;
}

export const rmaApi = {
  listar: (estado?: string) =>
    apiClient.get<DevolucionRMA[]>(`/api/rma${estado ? `?estado=${encodeURIComponent(estado)}` : ''}`),

  solicitar: (req: RegistrarRmaRequest) =>
    apiClient.post<DevolucionRMA>('/api/rma', req),

  inspeccionar: (id: string, req: DictaminarRmaRequest) =>
    apiClient.post<DevolucionRMA>(`/api/rma/${id}/inspeccionar`, req),
};
