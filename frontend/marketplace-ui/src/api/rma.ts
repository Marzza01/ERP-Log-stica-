/**
 * ERP desarrollado por Marcelo Zepeda
 * Todos los derechos reservados © 2026
 */

import { apiClient } from './client';
import type { DevolucionRMA } from '../types';

export interface RegistrarRmaRequest {
  ordenId: string;
  motivo: string;
  descripcion: string;
  solicitaReembolso: boolean;
  montoReclamado: number;
}

export interface DictaminarRmaRequest {
  dictamen: 'ReStock' | 'Scrap' | 'Remate';
  observacionesTecnicas?: string;
  aprobado: boolean;
  reembolsoAutorizado: number;
}

export const rmaApi = {
  listar: (estado?: string) =>
    apiClient.get<DevolucionRMA[]>(`/api/rma${estado ? `?estado=${estado}` : ''}`),

  solicitar: (req: RegistrarRmaRequest) =>
    apiClient.post<DevolucionRMA>('/api/rma', req),

  inspeccionar: (id: string, req: DictaminarRmaRequest) =>
    apiClient.post<DevolucionRMA>(`/api/rma/${id}/inspeccionar`, req),
};
