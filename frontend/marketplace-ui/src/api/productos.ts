/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import { apiClient } from './client';
import type { Producto } from '../types';

export interface ProductosResponse {
  total: number;
  pagina: number;
  tamano: number;
  items: Producto[];
}

export interface CrearProductoRequest {
  sku: string;
  nombre: string;
  descripcion: string;
  categoriaNombre: string;
  subcategoria: string;
  precioB2C: number;
  precioB2B: number;
  stockMinimo: number;
  proveedorNombre: string;
  paisOrigen: string;
  pesoKg: number;
  dimensionesCm: string;
  unidadMedida: string;
  estadoProducto: string;
  estaPublicado: boolean;
}

export interface AlertasStockResponse {
  totalAlertas: number;
  alertas: Array<{
    id: string;
    sku: string;
    nombre: string;
    categoriaNombre: string;
    proveedorNombre: string;
    stockMinimo: number;
    puntoReorden: number;
    cantidadDisponible: number;
    nivelCriticidad: string;
    ubicacionWMS: string;
  }>;
}

/** La API expone Stocks por ubicación; la UI usa el resumen stock por SKU. */
export function normalizarProducto(producto: Producto & {
  stocks?: Array<{ cantidadTotal?: number; cantidadReservada?: number; cantidadBloqueada?: number; puntoReorden?: number }>;
}): Producto {
  if (!Array.isArray(producto.stocks)) return producto;
  const totals = producto.stocks.reduce((acc, stock) => ({
    total: acc.total + (Number(stock.cantidadTotal) || 0),
    reserved: acc.reserved + (Number(stock.cantidadReservada) || 0),
    blocked: acc.blocked + (Number(stock.cantidadBloqueada) || 0),
  }), { total: 0, reserved: 0, blocked: 0 });
  const disponible = Math.max(0, totals.total - totals.reserved - totals.blocked);
  const { stocks: _stocks, ...rest } = producto;
  void _stocks;
  return {
    ...rest,
    stock: {
      id: producto.id,
      productoId: producto.id,
      cantidadTotal: totals.total,
      cantidadReservada: totals.reserved,
      cantidadDisponible: disponible,
      puntoReorden: producto.stockMinimo,
      esBajoMinimo: disponible <= producto.stockMinimo,
      ubicacionCodigo: producto.stocks.length > 1 ? 'Varias ubicaciones' : '',
      ultimaActualizacion: producto.fechaCreacion,
    },
  };
}

export function normalizarPaginaProductos(response: ProductosResponse): ProductosResponse {
  return { ...response, items: (response.items ?? []).map(normalizarProducto) };
}

const DEFAULT_VENDEDOR_ID = '11111111-1111-1111-1111-111111111111';
const DEFAULT_CATEGORIA_ID = '22222222-2222-2222-2222-222222222222';

const toBackendProducto = (req: CrearProductoRequest) => ({
  ...req,
  id: '00000000-0000-0000-0000-000000000000',
  precioBase: req.precioB2C,
  moneda: 'USD',
  stockInicial: 0,
  codigoEAN: '', // Pendiente para el integrante responsable de códigos de barras.
  vendedorId: DEFAULT_VENDEDOR_ID,
  categoriaId: DEFAULT_CATEGORIA_ID,
});

export const productosApi = {
  listar: (pagina = 1, tamano = 50) =>
    apiClient.get<ProductosResponse>(`/api/productos?pagina=${pagina}&tamano=${tamano}`).then(normalizarPaginaProductos),

  buscar: (params: {
    q?: string;
    categoria?: string;
    subcategoria?: string;
    precioMin?: number;
    precioMax?: number;
    soloConStock?: boolean;
    pagina?: number;
    tamano?: number;
  }) => {
    const q = new URLSearchParams();
    if (params.q) q.set('q', params.q);
    if (params.categoria) q.set('categoria', params.categoria);
    if (params.subcategoria) q.set('subcategoria', params.subcategoria);
    if (params.precioMin !== undefined) q.set('precioMin', String(params.precioMin));
    if (params.precioMax !== undefined) q.set('precioMax', String(params.precioMax));
    if (params.soloConStock !== undefined) q.set('soloConStock', String(params.soloConStock));
    if (params.pagina) q.set('pagina', String(params.pagina));
    if (params.tamano) q.set('tamano', String(params.tamano));
    return apiClient.get<ProductosResponse>(`/api/productos/buscar?${q.toString()}`).then(normalizarPaginaProductos);
  },

  obtener: (id: string) =>
    apiClient.get<Producto>(`/api/productos/${id}`),

  crear: (req: CrearProductoRequest) =>
    apiClient.post<Producto>('/api/productos', toBackendProducto(req)),

  actualizar: (id: string, req: CrearProductoRequest) =>
    apiClient.put<Producto>(`/api/productos/${id}`, req),

  eliminar: (id: string) =>
    apiClient.delete<void>(`/api/productos/${id}`),

  alertasReorden: () =>
    apiClient.get<AlertasStockResponse>('/api/productos/alertas-reorden'),
};
