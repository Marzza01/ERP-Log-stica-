import { hayOperacionPendiente } from './carritoV2Qa';

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
  // Una marca V1 ilegible O una operación V2 bloquean esta compra.
  // No sobrescribir ni reparar silenciosamente datos de pagos inciertos.
  if (!cuenta?.trim() || !pendienteValido(pendiente))
    throw new Error('INTENTO_V1_INVALIDO');
  if (hayOperacionPendiente(cuenta))
    throw new Error('INTENTO_PREVIO_SIN_CONCILIAR');
  localStorage.setItem(key(cuenta, 'pendiente'), JSON.stringify(pendiente));
  if (localStorage.getItem(key(cuenta, 'pendiente')) !== JSON.stringify(pendiente))
    throw new Error('INTENTO_V1_NO_PERSISTIDO');
}

/** Solo añade el ID confirmado a la MISMA clave ya guardada. */
export function actualizarPendiente(cuenta: string, pendiente: CheckoutPendiente): void {
  const anterior = leerPendiente(cuenta);
  if (!pendienteValido(pendiente) || !anterior ||
      anterior.clave !== pendiente.clave ||
      anterior.productoId !== pendiente.productoId ||
      anterior.cantidad !== pendiente.cantidad ||
      anterior.creadoUtc !== pendiente.creadoUtc ||
      (anterior.ordenId && anterior.ordenId !== pendiente.ordenId))
    throw new Error('ACTUALIZACION_V1_NO_AUTORIZADA');
  localStorage.setItem(key(cuenta, 'pendiente'), JSON.stringify(pendiente));
}

function pendienteValido(p: CheckoutPendiente): boolean {
  return !!p && KEY_REGEX.test(p.clave) && esGuid(p.productoId) &&
    typeof p.productoNombre === 'string' && !!p.productoNombre.trim() &&
    typeof p.creadoUtc === 'string' && !!p.creadoUtc &&
    Number.isSafeInteger(p.cantidad) && p.cantidad >= 1 && p.cantidad <= 9999 &&
    (p.ordenId === undefined || esGuid(p.ordenId));
}

export function borrarPendiente(cuenta: string, claveEsperada: string): void {
  // Otra pestaña pudo guardar una operación diferente durante el POST.
  // Nunca borrar la clave ajena solo porque terminó esta petición.
  const actual = leerPendiente(cuenta);
  if (!actual || !KEY_REGEX.test(claveEsperada) || actual.clave !== claveEsperada)
    throw new Error('INTENTO_V1_CAMBIO_ENTRE_PESTANAS');
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

export function pendienteResuelto(
  estadoOrden: string, estadoPago: string, estadoReembolso: string | null,
  reservaPendiente: number, requiereRevision = true,
): boolean {
  // Falla cerrado si faltan datos o la API reporta alertas, incluso si
  // el estado parece terminal. CanceladoAntesEnvio solo procede con
  // seguimiento respaldado por auditoría sin ninguna anomalía.
  if (requiereRevision !== false || !Number.isSafeInteger(reservaPendiente) ||
      reservaPendiente !== 0) return false;
  if (estadoOrden === 'Cancelada') {
    return estadoPago === 'Rechazado' || estadoPago === 'SinIntento' ||
      estadoPago === 'CanceladoAntesEnvio' || estadoReembolso === 'Finalizado';
  }
  return ['Aprobada', 'EnProceso', 'Enviada', 'Entregada'].includes(estadoOrden) &&
    estadoPago === 'Completado';
}
