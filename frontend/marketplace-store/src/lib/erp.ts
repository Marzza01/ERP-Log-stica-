import "server-only";
import { randomUUID } from "node:crypto";

/**
 * Conexión con el ERP/OMS de LOGIMKT (API .NET de Marcelo Zepeda, repositorio Marzza01/ERP-Log-stica-).
 *
 * Variables de entorno:
 *   ERP_API_URL   URL base de la API, p. ej. https://marketplaceerp-api.onrender.com
 *   ERP_EMAIL / ERP_PASSWORD              usuario del ERP con rol Comprador: crea los pedidos en el OMS
 *   ERP_ADMIN_EMAIL / ERP_ADMIN_PASSWORD  (opcional) usuario Admin: el catálogo trae también stock y precio B2B
 *
 * Si no están definidas, la tienda funciona sola con su propio inventario.
 */

export type ErpConfig = { url: string; email: string; password: string };

/** "pedidos" usa Comprador; "catalogo" prefiere Admin; "admin" exige credenciales Admin. */
export function erpConfig(kind: "pedidos" | "catalogo" | "admin" = "pedidos"): ErpConfig | null {
  const url = process.env.ERP_API_URL?.trim().replace(/\/+$/, "");
  const hasAdmin = Boolean(process.env.ERP_ADMIN_EMAIL?.trim() && process.env.ERP_ADMIN_PASSWORD);
  if (kind === "admin") {
    if (!url || !hasAdmin) return null;
    return { url, email: process.env.ERP_ADMIN_EMAIL!.trim(), password: process.env.ERP_ADMIN_PASSWORD! };
  }
  const useAdmin = kind === "catalogo" && hasAdmin;
  const email = (useAdmin ? process.env.ERP_ADMIN_EMAIL : process.env.ERP_EMAIL)?.trim();
  const password = useAdmin ? process.env.ERP_ADMIN_PASSWORD : process.env.ERP_PASSWORD;
  if (!url || !email || !password) return null;
  return { url, email, password };
}

export class ErpError extends Error {
  constructor(message: string, public status = 0) {
    super(message);
  }
}

type Session = { token: string; role: string; expires: number };
declare global {
  var __erpSessions: Map<string, Session> | undefined;
}
const sessions = () => (globalThis.__erpSessions ??= new Map());

const TIMEOUT_MS = 60_000; // Render puede tardar en "despertar" el servicio gratuito.

async function call<T>(cfg: ErpConfig, path: string, init: RequestInit = {}, token?: string): Promise<T> {
  const headers = new Headers(init.headers);
  headers.set("Accept", "application/json");
  if (init.body) headers.set("Content-Type", "application/json");
  if (token) headers.set("Authorization", `Bearer ${token}`);
  let res: Response;
  try {
    res = await fetch(cfg.url + path, { ...init, headers, cache: "no-store", signal: AbortSignal.timeout(TIMEOUT_MS) });
  } catch (e) {
    throw new ErpError(`No se pudo contactar al ERP (${(e as Error).message}).`);
  }
  const text = await res.text();
  let data: unknown = null;
  try {
    data = text ? JSON.parse(text) : null;
  } catch {
    data = text;
  }
  if (!res.ok) {
    const msg =
      (data && typeof data === "object" && ("mensaje" in data ? String(data.mensaje) : "tipo" in data ? String(data.tipo) : "")) ||
      `HTTP ${res.status}`;
    throw new ErpError(`El ERP respondió: ${msg}`, res.status);
  }
  return data as T;
}

/** Inicia sesión en el ERP y guarda el token mientras sea válido. */
export async function erpLogin(cfg: ErpConfig, force = false) {
  const s = sessions().get(cfg.email);
  if (!force && s && s.expires > Date.now() + 60_000) return s;
  const r = await call<{ token: string; role?: string; rol?: string; expiresAt?: string }>(cfg, "/api/auth/login", {
    method: "POST",
    body: JSON.stringify({ email: cfg.email, password: cfg.password }),
  });
  const expires = r.expiresAt ? Date.parse(r.expiresAt) : Date.now() + 30 * 60_000;
  const session: Session = { token: r.token, role: r.role ?? r.rol ?? "", expires };
  sessions().set(cfg.email, session);
  return session;
}

async function authed<T>(cfg: ErpConfig, path: string, init: RequestInit = {}): Promise<T> {
  const s = await erpLogin(cfg);
  try {
    return await call<T>(cfg, path, init, s.token);
  } catch (e) {
    if (e instanceof ErpError && e.status === 401) {
      const fresh = await erpLogin(cfg, true);
      return call<T>(cfg, path, init, fresh.token);
    }
    throw e;
  }
}

/* ---------- Catálogo ---------- */

type ErpStock = { cantidadTotal?: number; cantidadReservada?: number; cantidadBloqueada?: number; cantidadDisponible?: number };
export type ErpProduct = {
  id: string;
  sku: string;
  nombre: string;
  precioB2C: number;
  precioB2B?: number;
  estadoProducto?: string;
  estaPublicado?: boolean;
  disponibilidad?: boolean;
  imagenUrl?: string | null;
  categoriaNombre?: string;
  subcategoria?: string;
  descripcion?: string;
  codigoEAN?: string | null;
  proveedorNombre?: string;
  paisOrigen?: string;
  pesoKg?: number;
  dimensionesCm?: string;
  unidadMedida?: string;
  stockMinimo?: number;
  stock?: ErpStock | null;
  stocks?: ErpStock[];
};

function available(p: ErpProduct): number {
  const list = p.stocks?.length ? p.stocks : p.stock ? [p.stock] : [];
  // En el endpoint Admin, una lista vacía significa que el SKU no tiene existencias físicas.
  return list.length ? list.reduce((sum, s) => {
    const d = s.cantidadDisponible ?? (s.cantidadTotal ?? 0) - (s.cantidadReservada ?? 0) - (s.cantidadBloqueada ?? 0);
    return sum + Math.max(0, d);
  }, 0) : 0;
}

/** Descarga el catálogo publicado del ERP (todas las páginas). */
export async function erpCatalog(cfg: ErpConfig) {
  const out: (ErpProduct & { disponible: number })[] = [];
  let expectedTotal = 0;
  for (let page = 1; page <= 20; page++) {
    const r = await authed<{ total: number; items: ErpProduct[] }>(cfg, `/api/productos?pagina=${page}&tamano=500&orden=sku`);
    expectedTotal = r.total ?? expectedTotal;
    for (const p of r.items ?? []) out.push({ ...p, disponible: available(p) });
    if (!r.items?.length || out.length >= expectedTotal) break;
  }
  if (out.length < expectedTotal) {
    throw new ErpError(`Catálogo incompleto: el ERP anunció ${expectedTotal} productos y se recibieron ${out.length}.`);
  }
  return out;
}

/* ---------- Pedidos (OMS) ---------- */

export type ErpMarketplaceOrderInput = {
  externalOrderCode: string;
  customerType: "B2B" | "B2C";
  customerName: string;
  customerEmail: string;
  customerPhone?: string | null;
  company?: string | null;
  taxId?: string | null;
  deliveryAddress: string;
  city: string;
  requestedPaymentMethod: string;
  items: { sku: string; quantity: number }[];
};

/**
 * Registra una proforma en el OMS (rol Comprador). El ERP vuelve a resolver
 * precio, IVA, vendedor y stock por SKU. No ejecuta pasarelas ni cobros reales.
 */
export async function erpCreateOrder(cfg: ErpConfig, order: ErpMarketplaceOrderInput, key: string = randomUUID()) {
  const r = await authed<{
    erpOrderId?: string;
    erpOrderNumber?: string;
    status?: string;
    subtotal?: number;
    tax?: number;
    shipping?: number;
    total?: number;
  }>(cfg, "/api/integraciones/marketplace/pedidos", {
    method: "POST",
    headers: { "Idempotency-Key": key.replace(/[^A-Za-z0-9_-]/g, "") },
    body: JSON.stringify(order),
  });
  if (!r.erpOrderId) throw new ErpError("El ERP no devolvió el identificador de la orden.");
  return {
    id: r.erpOrderId,
    number: r.erpOrderNumber ?? null,
    status: r.status ?? "Pendiente",
    subtotal: r.subtotal ?? null,
    tax: r.tax ?? null,
    shipping: r.shipping ?? null,
    total: r.total ?? null,
  };
}

/** Cambios OMS, cancelaciones y RMA requieren la cuenta ERP Admin. */
export async function erpTransitionOrder(
  cfg: ErpConfig, code: string, newStatus: string, trackingCode?: string,
) {
  return authed<unknown>(cfg, "/api/ordenes/marketplace/" + encodeURIComponent(code) + "/estado", {
    method: "PUT",
    body: JSON.stringify({ newStatus, trackingCode }),
  });
}

export async function erpCancelOrder(cfg: ErpConfig, code: string) {
  return authed<unknown>(cfg, "/api/integraciones/marketplace/pedidos/" + encodeURIComponent(code) + "/cancelar", {
    method: "POST",
  });
}

export async function erpRequestReturn(cfg: ErpConfig, code: string, returnCode: string, reason: string) {
  return authed<unknown>(cfg, "/api/integraciones/marketplace/pedidos/" + encodeURIComponent(code) + "/devolucion", {
    method: "POST",
    body: JSON.stringify({ externalReturnCode: returnCode, reason }),
  });
}

export async function erpVerifyTransfer(cfg: ErpConfig, code: string) {
  return authed<{ paymentStatus?: string }>(cfg, "/api/ordenes/marketplace/" + encodeURIComponent(code) + "/verificar-transferencia", {
    method: "POST",
  });
}

export async function erpAuthorizeCredit(cfg: ErpConfig, code: string) {
  return authed<{ paymentStatus?: string }>(cfg, "/api/ordenes/marketplace/" + encodeURIComponent(code) + "/autorizar-credito", {
    method: "POST",
  });
}

export async function erpConfirmCashOnDelivery(cfg: ErpConfig, code: string) {
  return authed<{ paymentStatus?: string }>(cfg, "/api/ordenes/marketplace/" + encodeURIComponent(code) + "/confirmar-cobro-contra-entrega", {
    method: "POST",
  });
}

export type ErpOrder = {
  id: string;
  numeroOrden: string;
  estado: string;
  montoTotal: number;
  transportista?: string;
  guiaEnvio?: string;
  fechaEntregaEstimada?: string | null;
  fechaEntregaReal?: string | null;
};

export async function erpGetOrder(cfg: ErpConfig, id: string) {
  return authed<ErpOrder>(cfg, `/api/ordenes/${encodeURIComponent(id)}`);
}

/** Prueba rápida de conexión para el panel de administración. */
export async function erpPing(cfg: ErpConfig) {
  const s = await erpLogin(cfg, true);
  const r = await authed<{ total: number }>(cfg, "/api/productos?pagina=1&tamano=1");
  return { role: s.role, products: r.total };
}
