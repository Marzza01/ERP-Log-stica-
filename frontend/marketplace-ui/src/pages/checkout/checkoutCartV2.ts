import { hayOperacionPendiente } from './carritoV2Qa';

export interface CheckoutCarritoV2Linea {
  productoId: string;
  cantidad: number;
}

export interface CheckoutPendienteV2 {
  clave: string;
  items: CheckoutCarritoV2Linea[];
  creadoUtc: string;
  ordenId?: string;
}

const PREFIX = 'logimkt_checkout_b2c_v2:';
const GUID = /^[a-f\d]{8}-[a-f\d]{4}-[a-f\d]{4}-[a-f\d]{4}-[a-f\d]{12}$/i;
const KEY = /^[A-Za-z0-9_-]{8,128}$/;

const storageKey = (cuenta: string) =>
  `${PREFIX}${cuenta.trim().toLowerCase()}:pendiente`;

function valido(value: unknown): value is CheckoutPendienteV2 {
  if (!value || typeof value !== 'object') return false;
  const p = value as Partial<CheckoutPendienteV2>;
  return typeof p.clave === 'string' && KEY.test(p.clave) &&
    Array.isArray(p.items) && p.items.length >= 2 && p.items.length <= 20 &&
    p.items.every(i => i && GUID.test(i.productoId) &&
      Number.isSafeInteger(i.cantidad) && i.cantidad >= 1 && i.cantidad <= 9999) &&
    new Set(p.items.map(i => i.productoId.toLowerCase())).size === p.items.length &&
    typeof p.creadoUtc === 'string' && !!p.creadoUtc &&
    (p.ordenId === undefined || (typeof p.ordenId === 'string' && GUID.test(p.ordenId)));
}

export function leerPendienteV2(cuenta: string): CheckoutPendienteV2 | null {
  try {
    const raw = localStorage.getItem(storageKey(cuenta));
    if (!raw) return null;
    const parsed: unknown = JSON.parse(raw);
    return valido(parsed) ? parsed : null;
  } catch {
    return null;
  }
}

export function guardarPendienteV2(cuenta: string, pendiente: CheckoutPendienteV2): void {
  if (!cuenta?.trim() || !valido(pendiente))
    throw new Error('INTENTO_V2_INVALIDO');
  // Incluye operaciones V1, V2 y registros históricos QA. No sobrescribirlas.
  if (hayOperacionPendiente(cuenta))
    throw new Error('INTENTO_PREVIO_SIN_CONCILIAR');
  const serialized = JSON.stringify(pendiente);
  localStorage.setItem(storageKey(cuenta), serialized);
  if (localStorage.getItem(storageKey(cuenta)) !== serialized)
    throw new Error('INTENTO_V2_NO_PERSISTIDO');
}

export function actualizarPendienteV2(cuenta: string, pendiente: CheckoutPendienteV2): void {
  const anterior = leerPendienteV2(cuenta);
  if (!valido(pendiente) || !anterior ||
      anterior.clave !== pendiente.clave ||
      anterior.creadoUtc !== pendiente.creadoUtc ||
      JSON.stringify(anterior.items) !== JSON.stringify(pendiente.items) ||
      (anterior.ordenId && anterior.ordenId !== pendiente.ordenId))
    throw new Error('ACTUALIZACION_V2_NO_AUTORIZADA');
  localStorage.setItem(storageKey(cuenta), JSON.stringify(pendiente));
}

export function borrarPendienteV2(cuenta: string, claveEsperada: string): void {
  const actual = leerPendienteV2(cuenta);
  if (!actual || !KEY.test(claveEsperada) || actual.clave !== claveEsperada)
    throw new Error('INTENTO_V2_CAMBIO_ENTRE_PESTANAS');
  localStorage.removeItem(storageKey(cuenta));
}
