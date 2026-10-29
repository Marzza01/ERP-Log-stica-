/** Persistencia conservadora de un Checkout: no reintentar un pago ambiguo. */
export interface CheckoutPendiente {
  clave: string;
  productoId: string;
  productoNombre: string;
  cantidad: number;
  creadoUtc: string;
  ordenId?: string;
}

const PREFIX = 'logimkt_checkout_b2c_v1:';
const KEY_REGEX = /^[A-Za-z0-9_-]{8,128}$/;
const GUID_REGEX = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export const esGuid = (valor: string): boolean => GUID_REGEX.test(valor);

function key(cuenta: string, sufijo: string) {
  // El mismo navegador puede alojar diferentes sesiones.
  return `${PREFIX}${cuenta.trim().toLowerCase()}:${sufijo}`;
}

export function nuevaClaveIdempotencia(): string {
  // Requiere entropia del sistema; no usar Date.now ni Math.random para claves de compra.
  return crypto.randomUUID().replace(/-/g, '');
}

export function leerPendiente(cuenta: string): CheckoutPendiente | null {
  try {
    const value = localStorage.getItem(key(cuenta, 'pendiente'));
    if (!value) return null;
    const p = JSON.parse(value) as CheckoutPendiente;
    if (!p || !KEY_REGEX.test(p.clave) || !esGuid(p.productoId) ||
      !Number.isSafeInteger(p.cantidad) || p.cantidad < 1 || p.cantidad > 9999 ||
      (p.ordenId !== undefined && !esGuid(p.ordenId))) return null;
    return p;
  } catch { return null; }
}

export function guardarPendiente(cuenta: string, pendiente: CheckoutPendiente): void {
  // Nunca emitir un POST si no se puede registrar primero la clave durable.
  localStorage.setItem(key(cuenta, 'pendiente'), JSON.stringify(pendiente));
}

export function borrarPendiente(cuenta: string): void {
  localStorage.removeItem(key(cuenta, 'pendiente'));
}

export function leerOrdenes(cuenta: string): string[] {
  try {
    const arr = JSON.parse(localStorage.getItem(key(cuenta, 'ordenes')) ?? '[]');
    return Array.isArray(arr) ? Array.from(new Set(arr.filter((v): v is string => typeof v === 'string' && esGuid(v)))).slice(0, 12) : [];
  } catch { return []; }
}

export function recordarOrden(cuenta: string, ordenId: string): string[] {
  if (!esGuid(ordenId)) return leerOrdenes(cuenta);
  const lista = [ordenId, ...leerOrdenes(cuenta).filter(id => id !== ordenId)].slice(0, 12);
  localStorage.setItem(key(cuenta, 'ordenes'), JSON.stringify(lista));
  return lista;
}

export function pendienteResuelto(estadoOrden: string, estadoPago: string, estadoReembolso: string | null, reservaPendiente: number): boolean {
  // Criterios conservadores: si hay reserva pendiente o situacion ambigua, bloquear un nuevo checkout.
  if (reservaPendiente > 0) return false;
  if (estadoOrden === 'Cancelada') {
    return estadoPago === 'Rechazado' || estadoPago === 'SinIntento' || estadoReembolso === 'Finalizado';
  }
  return ['Aprobada', 'EnProceso', 'Enviada', 'Entregada'].includes(estadoOrden) && estadoPago === 'Completado';
}
