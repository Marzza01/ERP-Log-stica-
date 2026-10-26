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
  cantidadRecogida: number;
}

export interface EmpacarRequest {
  ordenId: string;
  numeroOrden: string;
  pesoRealKg: number;
  largoCm: number;
  anchoCm: number;
  altoCm: number;
  transportista: string;
  departamentoDestino: string;
  municipioDestino: string;
  direccionEntrega: string;
  contactoReceptor: string;
  telefonoReceptor: string;
}

export interface EmpacarResponse {
  mensaje: string;
  numeroGuia: string;
  numeroOrden: string;
  cubicaje: {
    pesoRealKg: number;
    dimensiones: string;
    volumenM3: number;
  };
  destino: string;
  transportista: string;
}

export const fulfillmentApi = {
  listarPickLists: (estado?: string) =>
    apiClient.get<PickList[]>(`/api/fulfillment/picklists${estado ? `?estado=${encodeURIComponent(estado)}` : ''}`),

  generarPickList: (ordenId: string, operador?: string) =>
    apiClient.post<GenerarPickListResponse>(
      `/api/fulfillment/ordenes/${ordenId}/generar-picklist${operador ? `?operador=${encodeURIComponent(operador)}` : ''}`
    ),

  confirmarPicking: (pickListId: string, confirmaciones: ConfirmarItemPickingRequest[]) =>
    apiClient.post<{ mensaje: string; numeroPickList: string; estado: string }>(
      `/api/fulfillment/picklists/${pickListId}/confirmar`,
      confirmaciones
    ),

  listarPaquetes: (estado?: string) =>
    apiClient.get<Paquete[]>(`/api/fulfillment/paquetes${estado ? `?estado=${encodeURIComponent(estado)}` : ''}`),

  empacar: (ordenId: string, req: EmpacarRequest) =>
    apiClient.post<EmpacarResponse>(`/api/fulfillment/ordenes/${ordenId}/empacar`, req),

  despachar: (paqueteId: string) =>
    apiClient.post<{ mensaje: string; numeroGuia: string; transportista: string; fechaDespacho?: string; estadoOrden?: string }>(
      `/api/fulfillment/paquetes/${paqueteId}/despachar`
    ),
};
