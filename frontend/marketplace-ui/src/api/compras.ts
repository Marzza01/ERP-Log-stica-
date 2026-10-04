/**
 * ERP desarrollado por Marcelo Zepeda
 * Todos los derechos reservados © 2026
 */

import { apiClient } from './client';
import type { OrdenCompra, MatchTridireccional } from '../types';

export interface POListResponse {
  total: number;
  pagina: number;
  tamano: number;
  ordenesCompra: OrdenCompra[];
}

export interface ItemPORequest {
  sku: string;
  nombreProducto: string;
  cantidad: number;
  precioUnitario: number;
}

export interface CrearPORequest {
  proveedorId: string;
  proveedorNombre: string;
  terminosPago?: string;
  observaciones?: string;
  fechaEntregaEsperada?: string;
  items: ItemPORequest[];
}

export interface RegistrarFacturaRequest {
  ordenCompraId: string;
  numeroFactura: string;
  subTotal: number;
  montoTotal: number;
  fechaEmision?: string;
  fechaVencimiento?: string;
}

export interface Validar3WayMatchRequest {
  ordenCompraId: string;
  facturaProveedorId: string;
  cantidadRecibidaBodega: number;
  toleranciaPorcentaje?: number;
}

export const comprasApi = {
  listarPO: (params?: { estado?: string; pagina?: number; tamano?: number }) => {
    const q = new URLSearchParams();
    if (params?.estado) q.set('estado', params.estado);
    if (params?.pagina) q.set('pagina', String(params.pagina));
    if (params?.tamano) q.set('tamano', String(params.tamano));
    return apiClient.get<POListResponse>(`/api/compras/po?${q.toString()}`);
  },

  obtenerPO: (id: string) =>
    apiClient.get<OrdenCompra>(`/api/compras/po/${id}`),

  crearPO: (req: CrearPORequest) =>
    apiClient.post<any>('/api/compras/po', req),

  enviarPO: (id: string) =>
    apiClient.post<any>(`/api/compras/po/${id}/enviar`),

  recibirMercancia: (id: string, cantidadRecibida: number) =>
    apiClient.post<any>(`/api/compras/po/${id}/recibir`, { cantidadRecibida }),

  registrarFactura: (req: RegistrarFacturaRequest) =>
    apiClient.post<any>('/api/compras/facturas', req),

  ejecutarMatch: (req: Validar3WayMatchRequest) =>
    apiClient.post<MatchTridireccional>('/api/compras/match', req),

  historialMatch: () =>
    apiClient.get<MatchTridireccional[]>('/api/compras/match/historial'),
};
