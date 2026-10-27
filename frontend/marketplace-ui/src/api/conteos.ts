/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import { apiClient } from './client';
import type { EstadoAjusteInventario } from './ajustes';

export type EstadoConteoCiclico =
  | 'Planificado'
  | 'EnConteo'
  | 'RequiereReconteo'
  | 'Completado'
  | 'CompletadoConAjuste'
  | 'Cancelado';

export interface ConteoCiclico {
  id: string;
  stockFisicoId: string;
  productoId: string;
  sku: string;
  productoNombre: string;
  ubicacionCodigo: string;
  estado: EstadoConteoCiclico;
  cantidadSistema?: number;
  primerConteo?: number;
  segundoConteo?: number;
  stockActual: number;
  ajusteInventarioId?: string;
  estadoAjuste?: EstadoAjusteInventario;
  observaciones?: string;
  creadoPor?: string;
  iniciadoPor?: string;
  contadoPor?: string;
  recontadoPor?: string;
  fechaCreacion: string;
  fechaInicio?: string;
  fechaPrimerConteo?: string;
  fechaReconteo?: string;
  fechaFinalizacion?: string;
}

export interface ConteoCiclicoResult {
  exitoso: boolean;
  conteo?: ConteoCiclico;
  mensaje: string;
}

export const conteosApi = {
  listar: (estado?: EstadoConteoCiclico) =>
    apiClient.get<ConteoCiclico[]>(`/api/inventario/conteos-ciclicos${estado ? `?estado=${estado}` : ''}`),

  planificar: (input: { stockFisicoId: string; observaciones?: string }) =>
    apiClient.post<ConteoCiclicoResult>('/api/inventario/conteos-ciclicos', input),

  iniciar: (id: string) =>
    apiClient.post<ConteoCiclicoResult>(`/api/inventario/conteos-ciclicos/${id}/iniciar`),

  primerConteo: (id: string, cantidad: number) =>
    apiClient.post<ConteoCiclicoResult>(`/api/inventario/conteos-ciclicos/${id}/primer-conteo`, { cantidad }),

  reconteo: (id: string, cantidad: number) =>
    apiClient.post<ConteoCiclicoResult>(`/api/inventario/conteos-ciclicos/${id}/reconteo`, { cantidad }),

  cancelar: (id: string) =>
    apiClient.post<ConteoCiclicoResult>(`/api/inventario/conteos-ciclicos/${id}/cancelar`),
};
