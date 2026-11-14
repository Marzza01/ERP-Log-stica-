/**
 * Diseño recuperado del carrito histórico, reconstruido exclusivamente para
 * validación QA. SIN import en las páginas, sin flag activo y sin llamadas HTTP.
 *
 * Diferencias de seguridad respecto de fix/auditoria/CheckoutPage.tsx:
 * - Nunca crea productos demo ni usa IDs inventados como fallback.
 * - El navegador envía solo productoId + cantidad. Precio, IVA, envío, vendedor
 *   y moneda oficiales proceden del servidor.
 * - Se impide mezclar vendedores o SKU inválidos antes de pedir cotización.
 * - No se crea ninguna nueva operación si existe un pago V1 o V2 pendiente.
 * - No emite ni reintenta pagos; guarda una clave antes de cualquier POST futuro.
 */
export type LineaV2 = Readonly<{ productoId: string; cantidad: number }>;

export interface ProductoCatalogoV2 {
  id: string;
  vendedorId: string;
  precioB2C: number;
  moneda: string;
  estaPublicado: boolean;
  estadoProducto: string;
}

export interface PendienteV2 {
  clave: string;
  items: LineaV2[];
  creadoUtc: string;
  ordenId?: string;
}

const PREFIX_V1 = 'logimkt_checkout_b2c_v1:';
const PREFIX_V2 = 'logimkt_checkout_b2c_v2_qa:';
const PREFIX_V2_COMERCIAL = 'logimkt_checkout_b2c_v2:';
const GUID = /^[a-f\d]{8}-[a-f\d]{4}-[a-f\d]{4}-[a-f\d]{4}-[a-f\d]{12}$/i;
const CLAVE = /^[A-Za-z0-9_-]{8,128}$/;
const MAX_SKU = 20;
const MAX_CANTIDAD = 9999;
const MAX_LINEAS = 200;

const key = (cuenta: string, version: string) =>
  version + cuenta.trim().toLowerCase() + ':pendiente';

export function productoDisponibleV2(p: ProductoCatalogoV2): boolean {
  return !!p && GUID.test(p.id) && GUID.test(p.vendedorId) &&
    p.id !== '00000000-0000-0000-0000-000000000000' &&
    p.vendedorId !== '00000000-0000-0000-0000-000000000000' &&
    p.estaPublicado === true &&
    p.estadoProducto.toLowerCase() === 'activo' &&
    p.moneda.toUpperCase() === 'USD' &&
    Number.isFinite(p.precioB2C) && p.precioB2C > 0;
}

/** Modelo sin dinero: el vendedor solo se utiliza para compatibilidad de UX. */
export function normalizarCarritoV2(
  entradas: readonly LineaV2[],
  catalogo: readonly ProductoCatalogoV2[],
): LineaV2[] {
  if (!Array.isArray(entradas) || entradas.length < 2 || entradas.length > MAX_LINEAS)
    throw new Error('CARRITO_V2_INVALIDO');
  const productos = new Map<string, ProductoCatalogoV2>();
  for (const producto of catalogo) {
    if (productos.has(producto.id.toLowerCase()))
      throw new Error('CATALOGO_V2_ID_DUPLICADO');
    productos.set(producto.id.toLowerCase(), producto);
  }
  const cantidades = new Map<string, number>();
  let vendedor = '';
  for (const entrada of entradas) {
    const id = entrada.productoId?.toLowerCase();
    const producto = productos.get(id);
    if (!id || !GUID.test(id) || !producto || !productoDisponibleV2(producto) ||
        !Number.isSafeInteger(entrada.cantidad) ||
        entrada.cantidad < 1 || entrada.cantidad > MAX_CANTIDAD)
      throw new Error('LINEA_V2_NO_APTA');
    if (!vendedor) vendedor = producto.vendedorId.toLowerCase();
    else if (vendedor !== producto.vendedorId.toLowerCase())
      throw new Error('CARRITO_V2_MULTI_VENDEDOR');
    const suma = (cantidades.get(id) ?? 0) + entrada.cantidad;
    if (!Number.isSafeInteger(suma) || suma > MAX_CANTIDAD)
      throw new Error('CARRITO_V2_CANTIDAD_EXCEDIDA');
    cantidades.set(id, suma);
  }
  if (cantidades.size < 2 || cantidades.size > MAX_SKU)
    throw new Error('CARRITO_V2_SKU_FUERA_LIMITE');
  return Array.from(cantidades, ([productoId, cantidad]) => ({ productoId, cantidad }))
    .sort((a, b) => a.productoId.localeCompare(b.productoId));
}

function validarPendiente(p: unknown): p is PendienteV2 {
  if (!p || typeof p !== 'object') return false;
  const x = p as Partial<PendienteV2>;
  return typeof x.clave === 'string' && CLAVE.test(x.clave) &&
    Array.isArray(x.items) && x.items.length >= 2 && x.items.length <= MAX_SKU &&
    x.items.every(i => i && GUID.test(i.productoId) && Number.isSafeInteger(i.cantidad) &&
      i.cantidad >= 1 && i.cantidad <= MAX_CANTIDAD) &&
    new Set(x.items.map(i => i.productoId.toLowerCase())).size === x.items.length &&
    typeof x.creadoUtc === 'string' &&
    (x.ordenId === undefined || (typeof x.ordenId === 'string' && GUID.test(x.ordenId)));
}

/**
 * Deliberadamente conservador: un V1 ilegible/corrupto también bloquea V2.
 * No borrar V1 para "arreglar" la conversión: requiere revisión del comprador.
 */
export function hayOperacionPendiente(cuenta: string): boolean {
  if (!cuenta?.trim()) return true;
  try {
    return localStorage.getItem(key(cuenta, PREFIX_V1)) !== null ||
      localStorage.getItem(key(cuenta, PREFIX_V2)) !== null ||
      localStorage.getItem(key(cuenta, PREFIX_V2_COMERCIAL)) !== null;
  } catch {
    return true;
  }
}

export function leerPendienteV2(cuenta: string): PendienteV2 | null {
  try {
    const raw = localStorage.getItem(key(cuenta, PREFIX_V2));
    if (!raw) return null;
    const obj: unknown = JSON.parse(raw);
    return validarPendiente(obj) ? obj : null;
  } catch { return null; }
}

/** Si guardar falla, nunca permitir emitir el POST del carrito. */
export function guardarPendienteV2(cuenta: string, pendiente: PendienteV2): void {
  if (!cuenta?.trim() || !validarPendiente(pendiente))
    throw new Error('INTENTO_V2_INVALIDO');
  if (hayOperacionPendiente(cuenta))
    throw new Error('INTENTO_PREVIO_SIN_CONCILIAR');
  localStorage.setItem(key(cuenta, PREFIX_V2), JSON.stringify(pendiente));
}

export function payloadV2(items: readonly LineaV2[]): { items: LineaV2[] } {
  // La cotización oficial debe provenir del servidor: no incluir importe.
  if (!Array.isArray(items) || items.length < 2 || items.length > MAX_SKU ||
    new Set(items.map(i => i.productoId)).size !== items.length ||
    items.some(i => !GUID.test(i.productoId) || !Number.isSafeInteger(i.cantidad) ||
      i.cantidad < 1 || i.cantidad > MAX_CANTIDAD))
    throw new Error('CARRO_NO_NORMALIZADO');
  return { items: items.map(i => ({ productoId: i.productoId, cantidad: i.cantidad })) };
}
