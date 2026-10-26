/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import { apiClient } from './client';
import type { OrdenCompra, MatchTridireccional, EstadoOrdenCompra, Estado3WayMatch } from '../types';

interface BackendItemOrdenCompra {
  id: string;
  productoId: string;
  sku: string;
  nombreProducto: string;
  cantidadSolicitada: number;
  cantidadRecibida: number;
  precioUnitarioAcordado: number;
  totalLinea: number;
}

interface BackendOrdenCompra {
  id: string;
  numeroPO: string;
  proveedorId: string;
  proveedorNombre: string;
  estado: number | string;
  montoTotal: number;
  moneda: string;
  terminosPago: string;
  fechaEmision: string;
  fechaEntregaEsperada?: string;
  items: BackendItemOrdenCompra[];
}

interface BackendMatch {
  id: string;
  ordenCompraId: string;
  resultado: number | string;
  cantidadOrdenadaPO: number;
  cantidadRecibidaBodega: number;
  montoTotalPO: number;
  montoTotalFactura: number;
  diferenciaMonto: number;
  detalleDiscrepancias: string;
  fechaAuditoria: string;
}

interface BackendPOListResponse {
  total: number;
  pagina: number;
  tamano: number;
  ordenesCompra: BackendOrdenCompra[];
}

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
  enviarInmediatamente?: boolean;
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

const mapEstadoPO = (estado: number | string): EstadoOrdenCompra => {
  const value = typeof estado === 'number'
    ? ['Borrador', 'Enviada', 'RecibidaParcial', 'RecibidaCompleta', 'Facturada', 'Pagada', 'Cancelada'][estado]
    : estado;

  if (value === 'Enviada') return 'Emitida';
  if (value === 'RecibidaParcial' || value === 'RecibidaCompleta') return 'Recibida';
  if (value === 'Facturada' || value === 'Pagada') return 'Cerrada';
  if (value === 'Cancelada') return 'Cancelada';
  return 'Borrador';
};

const mapOrden = (po: BackendOrdenCompra): OrdenCompra => ({
  id: po.id,
  numeroPO: po.numeroPO,
  proveedorId: po.proveedorId,
  nombreProveedor: po.proveedorNombre,
  estado: mapEstadoPO(po.estado),
  montoTotal: po.montoTotal,
  moneda: po.moneda,
  condicionPago: po.terminosPago,
  fechaEmision: po.fechaEmision,
  fechaEntregaEsperada: po.fechaEntregaEsperada,
  items: (po.items ?? []).map(item => ({
    id: item.id,
    productoId: item.productoId,
    descripcion: item.nombreProducto,
    cantidad: item.cantidadSolicitada,
    precioUnitario: item.precioUnitarioAcordado,
    total: item.totalLinea,
  })),
});

const mapEstadoMatch = (estado: number | string): Estado3WayMatch => {
  const value = typeof estado === 'number'
    ? ['Pendiente', 'Conforme', 'DiscrepanciaCantidad', 'DiscrepanciaPrecio', 'DiscrepanciaTotal', 'Rechazado'][estado]
    : estado;
  if (value === 'Conforme') return 'Conforme';
  if (value === 'Pendiente') return 'Pendiente';
  return 'Discrepante';
};

const mapMatch = (m: BackendMatch): MatchTridireccional => ({
  id: m.id,
  ordenCompraId: m.ordenCompraId,
  resultado: mapEstadoMatch(m.resultado),
  diferenciaCantidad: Math.abs((m.cantidadOrdenadaPO ?? 0) - (m.cantidadRecibidaBodega ?? 0)),
  diferenciaMonto: m.diferenciaMonto ?? Math.abs((m.montoTotalPO ?? 0) - (m.montoTotalFactura ?? 0)),
  observaciones: m.detalleDiscrepancias ?? '',
  fechaAuditoria: m.fechaAuditoria,
});

export const comprasApi = {
  listarPO: async (params?: { estado?: string; pagina?: number; tamano?: number }) => {
    const q = new URLSearchParams();
    if (params?.estado) q.set('estado', params.estado);
    if (params?.pagina) q.set('pagina', String(params.pagina));
    if (params?.tamano) q.set('tamano', String(params.tamano));
    const res = await apiClient.get<BackendPOListResponse>(`/api/compras/po?${q.toString()}`);
    return { ...res, ordenesCompra: (res.ordenesCompra ?? []).map(mapOrden) } satisfies POListResponse;
  },

  obtenerPO: async (id: string) => mapOrden(await apiClient.get<BackendOrdenCompra>(`/api/compras/po/${id}`)),

  crearPO: (req: CrearPORequest) =>
    apiClient.post('/api/compras/po', req),

  enviarPO: (id: string) =>
    apiClient.post(`/api/compras/po/${id}/enviar`),

  recibirMercancia: (id: string, cantidadRecibida: number, ubicacionBodega?: string, notas?: string) =>
    apiClient.put(`/api/compras/po/${id}/recibir`, { cantidadRecibida, ubicacionBodega, notas }),

  registrarFactura: (req: RegistrarFacturaRequest) =>
    apiClient.post('/api/compras/facturas', req),

  ejecutarMatch: async (req: Validar3WayMatchRequest) =>
    mapMatch(await apiClient.post<BackendMatch>('/api/compras/match', {
      ordenCompraId: req.ordenCompraId,
      facturaProveedorId: req.facturaProveedorId,
      cantidadRecibidaBodega: req.cantidadRecibidaBodega,
    })),

  historialMatch: async () =>
    (await apiClient.get<BackendMatch[]>('/api/compras/match/historial')).map(mapMatch),
};
