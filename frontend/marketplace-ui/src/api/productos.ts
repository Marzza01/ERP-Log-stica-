/**
 * ERP desarrollado por Marcelo Zepeda
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
  precio: number;
  costoUnitario?: number;
  categoria: string;
  marca?: string;
  pesoKg?: number;
  largoCm?: number;
  anchoCm?: number;
  altoCm?: number;
  estaPublicado?: boolean;
}

export const productosApi = {
  listar: (pagina = 1, tamano = 50) =>
    apiClient.get<ProductosResponse>(`/api/productos?pagina=${pagina}&tamano=${tamano}`),

  buscar: (params: { q?: string; categoria?: string; minPrecio?: number; maxPrecio?: number }) => {
    const q = new URLSearchParams();
    if (params.q) q.set('q', params.q);
    if (params.categoria) q.set('categoria', params.categoria);
    if (params.minPrecio) q.set('minPrecio', String(params.minPrecio));
    if (params.maxPrecio) q.set('maxPrecio', String(params.maxPrecio));
    return apiClient.get<Producto[]>(`/api/productos/buscar?${q.toString()}`);
  },

  obtener: (id: string) =>
    apiClient.get<Producto>(`/api/productos/${id}`),

  crear: (req: CrearProductoRequest) =>
    apiClient.post<Producto>('/api/productos', req),

  actualizar: (id: string, req: Partial<CrearProductoRequest>) =>
    apiClient.put<Producto>(`/api/productos/${id}`, req),

  eliminar: (id: string) =>
    apiClient.delete<void>(`/api/productos/${id}`),

  alertasReorden: () =>
    apiClient.get<any[]>('/api/productos/alertas-reorden'),
};
