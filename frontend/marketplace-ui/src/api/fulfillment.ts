/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import { apiClient } from './client';
import type { PickList, Paquete } from '../types';

export interface GenerarPickListResponse {
  mensaje: string;
  pickListId: string;
  numeroPickList: string;
  items: {
    id: string;
    sku: string;
    nombreProducto: string;
    cantidadSolicitada: number;
    ubicacion: string;
    ruta: string;
  }[];
}

export interface ConfirmarItemPickingRequest {
  itemPickListId: string;
  cantidadRecolectada: number;
  recogido: boolean;
  observacion?: string;
}

export interface EmpacarRequest {
  ordenId: string;
  numeroOrden: string;
  transportista: string;
  municipioDestino: string;
  departamentoDestino: string;
  pesoRealKg: number;
  largoCm: number;
  anchoCm: number;
  altoCm: number;
  observaciones?: string;
}

export const fulfillmentApi = {
  listarPickLists: (estado?: string) =>
    apiClient.get<PickList[]>(`/api/fulfillment/picklists${estado ? `?estado=${estado}` : ''}`),

  generarPickList: (ordenId: string, operador?: string) =>
    apiClient.post<GenerarPickListResponse>(
      `/api/fulfillment/ordenes/${ordenId}/generar-picklist${operador ? `?operador=${encodeURIComponent(operador)}` : ''}`
    ),

  confirmarPicking: (pickListId: string, confirmaciones: ConfirmarItemPickingRequest[]) =>
    apiClient.post<any>(`/api/fulfillment/picklists/${pickListId}/confirmar`, confirmaciones),

  listarPaquetes: (estado?: string) =>
    apiClient.get<Paquete[]>(`/api/fulfillment/paquetes${estado ? `?estado=${estado}` : ''}`),

  empacar: (ordenId: string, req: EmpacarRequest) =>
    apiClient.post<any>(`/api/fulfillment/ordenes/${ordenId}/empacar`, req),

  despachar: (paqueteId: string) =>
    apiClient.post<any>(`/api/fulfillment/paquetes/${paqueteId}/despachar`),
};
