/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import { apiClient } from './client';

export interface ProveedorLogistico {
  id: string;
  nombreComercial: string;
  razonSocial: string;
  categoriaSuministro: string;
  contactoPrincipal: string;
  email: string;
  telefono: string;
  direccion: string;
  plazoCreditoDias: number;
  otifHistorico: number;
  productosCatalogo: number;
  productosBajoMinimo: number;
  ordenesCompraAbiertas: number;
  unidadesPendientesRecepcion: number;
}

export interface ProveedoresLogisticaResponse {
  total: number;
  conProductosCatalogo: number;
  conCompraAbierta: number;
  nombresCatalogoNoVinculados: string[];
  items: ProveedorLogistico[];
}

export const proveedoresApi = {
  listar: (buscar?: string) => {
    const q = buscar?.trim() ? `?buscar=${encodeURIComponent(buscar.trim())}` : '';
    return apiClient.get<ProveedoresLogisticaResponse>(`/api/proveedores/logistica${q}`);
  },
};
