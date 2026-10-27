/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import { apiClient } from './client';

export type NivelReabastecimiento = 'AGOTADO' | 'BAJO_MINIMO' | 'EN_PUNTO_REORDEN';
export type EstadoCoberturaCompra = 'SIN_PO' | 'COBERTURA_PARCIAL' | 'CUBIERTA_POR_PO';

export interface NecesidadReabastecimiento {
  productoId: string;
  sku: string;
  nombre: string;
  categoriaNombre: string;
  proveedorNombre: string;
  proveedorVinculado: boolean;
  proveedorId?: string;
  proveedorMaestroNombre?: string;
  proveedorRazonSocial?: string;
  otifHistorico?: number;
  plazoCreditoDias?: number;
  contactoProveedor?: string;
  emailProveedor?: string;
  stockMinimo: number;
  cantidadFisica: number;
  cantidadReservada: number;
  cantidadBloqueada: number;
  cantidadDisponible: number;
  faltanteHastaMinimo: number;
  cantidadParaSalirAlerta: number;
  nivelCriticidad: NivelReabastecimiento;
  cantidadEnCompra: number;
  disponibleProyectado: number;
  faltanteProyectado: number;
  estadoCompra: EstadoCoberturaCompra;
  ordenesCompra: string[];
}

export interface ReabastecimientoResponse {
  total: number;
  agotados: number;
  conCompraEnCurso: number;
  sinCoberturaCompleta: number;
  items: NecesidadReabastecimiento[];
}

export const reabastecimientoApi = {
  necesidades: () =>
    apiClient.get<ReabastecimientoResponse>('/api/reabastecimiento/necesidades'),
};
