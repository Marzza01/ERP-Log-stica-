/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import { apiClient } from './client';

export type TipoAlertaInventario = 'LOW_STOCK' | 'CRITICAL_STOCK' | 'OUT_OF_STOCK';
export type PrioridadAlertaInventario = 'MEDIUM' | 'HIGH' | 'CRITICAL';
export type EstadoAlertaInventario = 'OPEN' | 'ACKNOWLEDGED';

export interface AlertaInventario {
  productoId: string;
  alertaId: string;
  sku: string;
  producto: string;
  categoria: string;
  tipo: TipoAlertaInventario;
  prioridad: PrioridadAlertaInventario;
  estado: EstadoAlertaInventario;
  cantidadDisponible: number;
  cantidadFisica: number;
  cantidadReservada: number;
  cantidadBloqueada: number;
  cantidadEnTransito: number;
  fechaEntregaEsperada?: string;
  ordenesCompra: string[];
  stockMinimo: number;
  puntoReorden: number;
  faltanteAReorden: number;
  ubicaciones: string;
  mensaje: string;
  recomendacion: string;
  reconocidaEn?: string;
  reconocidaPor?: string;
}

export interface AlertasInventarioResponse {
  total: number;
  pendientes: number;
  reconocidas: number;
  prioridadMedia: number;
  prioridadAlta: number;
  prioridadCritica: number;
  sinExistencia: number;
  faltanteAReorden: number;
  items: AlertaInventario[];
}

export const alertasApi = {
  listar: () => apiClient.get<AlertasInventarioResponse>('/api/alertas-inventario'),
  reconocer: (productoId: string, tipo: TipoAlertaInventario, usuario?: string) =>
    apiClient.post<void>(`/api/alertas-inventario/${productoId}/reconocer`, { tipo, usuario }),
};
