/**
 * LOGIMKT Checkout B2C. El servidor determina comprador, vendedor, moneda y precio.
 * No enviar montos ni aceptar ordenes de terceros sin autorizacion.
 */
import { apiClient } from './client';
import type { Producto } from '../types';

export interface CatalogoCheckout {
  total: number;
  pagina: number;
  tamano: number;
  items: Producto[];
}

export interface CheckoutResultado {
  ordenId: string;
  transaccionId: string;
  montoVendedor: number;
  montoMarketplace: number;
}

export interface CheckoutSeguimiento {
  ordenId: string;
  numeroOrden: string;
  estadoOrden: string;
  estadoPago: string;
  estadoReserva: string;
  estadoReembolso: string | null;
  cantidadReservadaPendiente: number;
  cantidadDespachada: number;
  montoTotal: number;
  moneda: string;
  requiereRevision: boolean;
  alertas: string[];
}

export interface CheckoutErrorResponse {
  tipo?: string;
  ordenId?: string;
  mensaje?: string;
}

export const checkoutApi = {
  catalogo: (filtro: string, pagina: number, tamano = 8) => {
    const q = new URLSearchParams({
      pagina: String(pagina),
      tamano: String(tamano),
      soloConStock: 'true',
    });
    if (filtro.trim()) q.set('q', filtro.trim());
    return apiClient.get<CatalogoCheckout>(`/api/productos/buscar?${q}`);
  },
  // Exactamente { productoId, cantidad }, identificacion y precio resueltos por la API.
  comprar: (productoId: string, cantidad: number, claveIdempotencia: string) =>
    apiClient.post<CheckoutResultado>(
      '/api/checkout',
      { productoId, cantidad },
      { 'Idempotency-Key': claveIdempotencia },
    ),
  seguimiento: (ordenId: string) =>
    apiClient.get<CheckoutSeguimiento>(`/api/checkout/ordenes/${encodeURIComponent(ordenId)}/seguimiento`),
};
