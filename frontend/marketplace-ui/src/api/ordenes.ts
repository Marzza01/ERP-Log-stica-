/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import { apiClient } from './client';
import type { Orden, OrdenesResponse, CrearOrdenRequest } from '../types';

export const ordenesApi = {
  listar: (params?: { estado?: string; pagina?: number; tamano?: number }) => {
    const qs = new URLSearchParams();
    if (params?.estado) qs.set('estado', params.estado);
    if (params?.pagina) qs.set('pagina', String(params.pagina));
    if (params?.tamano) qs.set('tamano', String(params.tamano));
    return apiClient.get<OrdenesResponse>(`/api/ordenes?${qs}`);
  },

  obtener: (id: string) =>
    apiClient.get<Orden>(`/api/ordenes/${id}`),

  crear: (data: CrearOrdenRequest) =>
    apiClient.post<{ id: string; numeroOrden: string; montoTotal: number }>('/api/ordenes', data),

  transicionarEstado: (id: string, nuevoEstado: string, guiaEnvio?: string) =>
    apiClient.put<object>(`/api/ordenes/${id}/estado`, { nuevoEstado, guiaEnvio }),

  getKpis: () =>
    apiClient.get<object>('/api/ordenes/kpis'),
};
export interface OrdenVendedorResumen {
  id: string;
  numeroOrden: string;
  estado: string;
  tipoCliente: string;
  montoTotal: number;
  moneda: string;
  fechaCreacion: string;
  totalItems: number;
}
export interface ItemOrdenVendedor {
  productoId: string;
  sku: string;
  nombre: string;
  cantidad: number;
  precioUnitario: number;
}
export interface OrdenVendedorDetalle extends Omit<OrdenVendedorResumen, 'totalItems'> {
  items: ItemOrdenVendedor[];
}
export interface MisOrdenesVendedorResponse {
  total: number;
  pagina: number;
  tamano: number;
  ordenes: OrdenVendedorResumen[];
}

// Endpoints de solo lectura, sin IDs de vendedor suministrados por el navegador.
export const misOrdenesVendedorApi = {
  listar: (params: { estado?: string; pagina: number; tamano: number }) => {
    const qs = new URLSearchParams({
      pagina: String(params.pagina),
      tamano: String(params.tamano),
    });
    if (params.estado) qs.set('estado', params.estado);
    return apiClient.get<MisOrdenesVendedorResponse>(`/api/ordenes/mis-ordenes?${qs}`);
  },
  detalle: (id: string) =>
    apiClient.get<OrdenVendedorDetalle>(`/api/ordenes/mis-ordenes/${encodeURIComponent(id)}`),
};
