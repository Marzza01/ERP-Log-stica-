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
  vendedorId?: string;
  categoriaId?: string;
}

export interface OpcionesAltaProducto {
  vendedores: Array<{ id: string; nombre: string }>;
  categorias: Array<{ id: string; nombre: string }>;
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

const GUID_REGISTRADO = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const ID_NULO = '00000000-0000-0000-0000-000000000000';
// Los IDs de muestra NO representan Core.Vendedores/Core.Categorias.
const IDS_PLANTILLA = new Set([
  '11111111-1111-1111-1111-111111111111',
  '22222222-2222-2222-2222-222222222222',
]);

function referenciaValida(id?: string): id is string {
  return typeof id === 'string' && GUID_REGISTRADO.test(id) &&
    id.toLowerCase() !== ID_NULO && !IDS_PLANTILLA.has(id.toLowerCase());
}

const toBackendProducto = (req: CrearProductoRequest) => {
  if (!referenciaValida(req.vendedorId) || !referenciaValida(req.categoriaId))
    throw new Error('Selecciona un vendedor activo y una categoría reales antes de crear el producto.');
  return {
  ...req,
  id: '00000000-0000-0000-0000-000000000000',
  precioBase: req.precioB2C,
  moneda: 'USD',
  stockInicial: 0,
  // Nunca crear una cadena vacía como código físico no verificado.
  codigoEAN: null,
  vendedorId: req.vendedorId,
  categoriaId: req.categoriaId,
  };
};

export const productosApi = {
  opcionesAlta: () => apiClient.get<OpcionesAltaProducto>('/api/productos/opciones-alta'),
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
