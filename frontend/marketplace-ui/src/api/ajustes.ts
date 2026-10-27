/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import { apiClient } from './client';

export type TipoAjusteInventario = 'Positivo' | 'Negativo';
export type EstadoAjusteInventario = 'PendienteAprobacion' | 'Aprobado' | 'Rechazado' | 'Aplicado' | 'Cancelado';

export interface AjusteInventario {
  id: string;
  stockFisicoId: string;
  productoId: string;
  sku: string;
  productoNombre: string;
  ubicacionCodigo: string;
  tipo: TipoAjusteInventario;
  cantidad: number;
  motivo: string;
  detalle?: string;
  referencia?: string;
  estado: EstadoAjusteInventario;
  stockActual: number;
  disponibleActual: number;
  creadoPor?: string;
  aprobadoPor?: string;
  aplicadoPor?: string;
  fechaCreacion: string;
  fechaDecision?: string;
  fechaAplicacion?: string;
}

export interface AjusteInventarioResult {
  exitoso: boolean;
  ajuste?: AjusteInventario;
  mensaje: string;
}

export const ajustesApi = {
  listar: (estado?: EstadoAjusteInventario) =>
    apiClient.get<AjusteInventario[]>(`/api/inventario/ajustes${estado ? `?estado=${estado}` : ''}`),

  crear: (input: {
    stockFisicoId: string;
    tipo: TipoAjusteInventario;
    cantidad: number;
    motivo: string;
    detalle?: string;
    referencia?: string;
  }) => apiClient.post<AjusteInventarioResult>('/api/inventario/ajustes', input),

  aprobar: (id: string) => apiClient.post<AjusteInventarioResult>(`/api/inventario/ajustes/${id}/aprobar`),
  rechazar: (id: string) => apiClient.post<AjusteInventarioResult>(`/api/inventario/ajustes/${id}/rechazar`),
  cancelar: (id: string) => apiClient.post<AjusteInventarioResult>(`/api/inventario/ajustes/${id}/cancelar`),
  aplicar: (id: string) => apiClient.post<AjusteInventarioResult>(`/api/inventario/ajustes/${id}/aplicar`),
};
