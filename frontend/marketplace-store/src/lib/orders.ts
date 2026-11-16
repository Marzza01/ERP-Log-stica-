import "server-only";
import { randomBytes } from "node:crypto";
import type { DbClient } from "./db";
import { query, tx } from "./db";
import {
  B2B_MIN_ORDER, B2C_FREE_SHIPPING_FROM, B2C_MIN_ORDER, B2C_SHIPPING, CARRIERS, PAYMENT_METHODS, PROMISE_DAYS, type Channel,
} from "./shared";

export type OrderInput = {
  channel: Channel;
  customer_name: string;
  customer_email: string;
  customer_phone?: string;
  company?: string;
  tax_id?: string;
  address: string;
  city: string;
  payment_method: string;
  items: { sku: string; qty: number }[];
};

export class OrderError extends Error {}


function paymentStatus(method: string): string {
  switch (method) {
    case "tarjeta": return "Pago con tarjeta pendiente de habilitación";
    case "transferencia": return "Pendiente de verificación";
    case "contra_entrega": return "Cobro contra entrega pendiente";
    case "credito_30": return "Crédito 30 días pendiente de autorización";
    default: return "Pendiente de validación";
  }
}

async function addEvent(c: DbClient, orderId: number, status: string, note: string) {
  await c.query("INSERT INTO order_events (order_id,status,note) VALUES ($1,$2,$3)", [orderId, status, note]);
}

/**
 * Flujo del pedido (Marketplace → OMS → ERP):
 * 1. Valida datos del cliente y reglas B2B/B2C.
 * 2. Bloquea los productos y verifica stock en tiempo real.
 * 3. Reserva (descuenta) inventario y registra el movimiento en el ERP.
 * 4. Crea el pedido en estado Recibido → Confirmado.
 */
export async function createOrder(input: OrderInput, erpRequired = false) {
  if (input.channel !== "B2B" && input.channel !== "B2C") throw new OrderError("Tipo de cliente no válido");
  const channel: Channel = input.channel;
  const req = (v: string | undefined, label: string) => {
    if (!v || !v.trim()) throw new OrderError(`Falta: ${label}`);
    return v.trim();
  };
  const name = req(input.customer_name, "nombre");
  const email = req(input.customer_email, "correo");
  if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(email)) throw new OrderError("El correo no es válido");
  const address = req(input.address, "dirección");
  const city = req(input.city, "ciudad");
  const company = channel === "B2B" ? req(input.company, "nombre de la empresa") : null;
  const taxId = channel === "B2B" ? req(input.tax_id, "NIT o NRC de la empresa") : null;
  if (!PAYMENT_METHODS[channel].some((m) => m.id === input.payment_method))
    throw new OrderError("Método de pago no válido para este tipo de cliente");

  const wanted = new Map<string, number>();
  for (const it of input.items ?? []) {
    const q = Math.floor(Number(it.qty));
    if (!it.sku || !Number.isFinite(q) || q <= 0) continue;
    wanted.set(it.sku, (wanted.get(it.sku) ?? 0) + Math.min(q, 9999));
  }
  if (!wanted.size) throw new OrderError("El carrito está vacío");
  if (wanted.size > 20) throw new OrderError("El carrito no puede superar 20 productos distintos.");

  return tx(async (c) => {
    // Bloqueo de filas: evita vender el mismo stock dos veces si llegan pedidos simultáneos.
    const { rows: products } = await c.query<{
      sku: string; name: string; price_b2c: number; price_b2b: number; stock: number; weight_kg: number | null; status: string;
    }>(
      "SELECT sku,name,price_b2c,price_b2b,stock,weight_kg,status FROM products WHERE sku = ANY($1) ORDER BY sku FOR UPDATE",
      [[...wanted.keys()]],
    );
    const bySku = new Map(products.map((product) => [product.sku, product]));
    if (erpRequired) {
      const unavailable = [...wanted.entries()].filter(([sku, qty]) => {
        const product = bySku.get(sku);
        return !product || product.status !== "Activo" || product.stock < qty;
      });
      if (unavailable.length) {
        throw new OrderError("El inventario local no confirma disponibilidad suficiente para: " +
          unavailable.map(([sku]) => sku).join(", ") + ". Sincroniza el catálogo y vuelve a intentarlo.");
      }
    }
    const lines = products
      .filter((p) => p.status === "Activo")
      .map((p) => {
        const requested = wanted.get(p.sku)!;
        const qty = erpRequired ? requested : Math.min(requested, p.stock);
        const price = channel === "B2B" ? p.price_b2b : p.price_b2c;
        if (price <= 0) throw new OrderError(`El producto ${p.sku} no tiene precio habilitado para ${channel}.`);
        return { ...p, requested, qty, price, total: Math.round(qty * price * 100) / 100 };
      });
    const sendable = lines.filter((l) => l.qty > 0);
    if (!sendable.length) throw new OrderError("Los productos del carrito no tienen stock disponible");

    const subtotal = Math.round(sendable.reduce((s, l) => s + l.total, 0) * 100) / 100;
    if (channel === "B2B" && subtotal < B2B_MIN_ORDER)
      throw new OrderError(`La compra mínima B2B es de $${B2B_MIN_ORDER}. Tu subtotal disponible es $${subtotal.toFixed(2)}.`);
    if (channel === "B2C" && subtotal < B2C_MIN_ORDER)
      throw new OrderError(`La compra mínima es de $${B2C_MIN_ORDER}.`);

    const weight = sendable.reduce((s, l) => s + (l.weight_kg ?? 0.5) * l.qty, 0);
    const tax = Math.round(subtotal * 0.13 * 100) / 100;
    const shipping = channel === "B2C" && subtotal < B2C_FREE_SHIPPING_FROM ? B2C_SHIPPING : 0;
    const total = Math.round((subtotal + tax + shipping) * 100) / 100;
    // Costo logístico interno estimado: base por pedido + costo por kg transportado.
    const logisticsCost = Math.round((2 + 0.25 * weight) * 100) / 100;
    const initialStatus = erpRequired ? "Pendiente ERP" : "Confirmado";

    const { rows: [order] } = await c.query<{ id: number }>(
      `INSERT INTO orders (channel,customer_name,customer_email,customer_phone,company,tax_id,address,city,
         payment_method,payment_status,subtotal,tax_amount,shipping_charge,total,logistics_cost,total_weight_kg,status,promised_at)
       VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17, now() + ($18 || ' days')::interval)
       RETURNING id`,
      [channel, name, email, input.customer_phone?.trim() || null, company, taxId, address, city,
       input.payment_method, paymentStatus(input.payment_method), subtotal, tax, shipping, total,
       logisticsCost, weight, initialStatus, String(PROMISE_DAYS[channel])],
    );
    const code = `LMK-${randomBytes(12).toString("hex").toUpperCase()}`;
    await c.query("UPDATE orders SET code=$1 WHERE id=$2", [code, order.id]);

    for (const l of lines) {
      await c.query(
        "INSERT INTO order_items (order_id,sku,name,qty_requested,qty,unit_price,line_total) VALUES ($1,$2,$3,$4,$5,$6,$7)",
        [order.id, l.sku, l.name, l.requested, l.qty, l.price, l.total],
      );
      if (l.qty > 0) {
        await c.query("UPDATE products SET stock = stock - $1, updated_at=now() WHERE sku=$2", [l.qty, l.sku]);
        await c.query("INSERT INTO inventory_movements (sku,type,qty,reference) VALUES ($1,'SALIDA_VENTA',$2,$3)", [
          l.sku, -l.qty, code,
        ]);
      }
    }
    const partial = !erpRequired && (lines.some((l) => l.qty < l.requested) || lines.length < wanted.size);
    await addEvent(c, order.id, "Recibido", "Pedido recibido desde el Marketplace.");
    await addEvent(
      c, order.id, initialStatus,
      erpRequired
        ? "Pendiente de validación de catálogo y reserva de inventario por el ERP. El pedido aún no está confirmado."
        : partial
          ? "Pedido confirmado en modo local con surtido parcial por falta de inventario."
          : "Pedido confirmado en modo local; no hay conexión con el ERP configurada.",
    );
    return { code, partial, pendingErp: erpRequired };
  });
}

const NEXT: Record<string, string> = {
  Confirmado: "En preparación",
  "En preparación": "Enviado",
  Enviado: "Entregado",
  Entregado: "Cerrado",
};

export function nextStatus(status: string): string | null {
  return NEXT[status] ?? null;
}

/** Avanza el pedido al siguiente estado del flujo OMS. */
export async function advanceOrder(code: string) {
  return tx(async (c) => {
    const { rows: [o] } = await c.query<{ id: number; status: string }>(
      "SELECT id,status FROM orders WHERE code=$1 FOR UPDATE", [code],
    );
    if (!o) throw new OrderError("Pedido no encontrado");
    const next = NEXT[o.status];
    if (!next) throw new OrderError(`El pedido está en "${o.status}" y no puede avanzar`);
    let note = "Notificación enviada al cliente.";
    if (next === "En preparación") {
      note = "Orden de preparación generada para el almacén (picking y empaque). " + note;
      await c.query("UPDATE orders SET status=$1 WHERE id=$2", [next, o.id]);
    } else if (next === "Enviado") {
      const carrier = CARRIERS[o.id % CARRIERS.length];
      const tracking = `GE${Date.now().toString().slice(-8)}${o.id}`;
      note = `Transportista asignado: ${carrier}. Guía de envío ${tracking}. ` + note;
      await c.query("UPDATE orders SET status=$1, carrier=$2, tracking=$3, shipped_at=now() WHERE id=$4", [
        next, carrier, tracking, o.id,
      ]);
    } else if (next === "Entregado") {
      note = "Entrega registrada. KPI OTIF actualizado. Encuesta de satisfacción enviada al cliente.";
      await c.query(
        `UPDATE orders SET status=$1, delivered_at=now() WHERE id=$2`,
        [next, o.id],
      );
    } else {
      note = "Pedido cerrado.";
      await c.query("UPDATE orders SET status=$1 WHERE id=$2", [next, o.id]);
    }
    await addEvent(c, o.id, next, note);
    return next;
  });
}

async function restock(c: DbClient, orderId: number, code: string, type: string) {
  const { rows } = await c.query<{ sku: string; qty: number }>(
    "SELECT sku, qty FROM order_items WHERE order_id=$1 AND qty > 0", [orderId],
  );
  for (const it of rows) {
    await c.query("UPDATE products SET stock = stock + $1, updated_at=now() WHERE sku=$2", [it.qty, it.sku]);
    await c.query("INSERT INTO inventory_movements (sku,type,qty,reference) VALUES ($1,$2,$3,$4)", [
      it.sku, type, it.qty, code,
    ]);
  }
}

export async function cancelOrder(code: string) {
  return tx(async (c) => {
    const { rows: [o] } = await c.query<{ id: number; status: string }>(
      "SELECT id,status FROM orders WHERE code=$1 FOR UPDATE", [code],
    );
    if (!o) throw new OrderError("Pedido no encontrado");
    if (!["Recibido", "Pendiente ERP", "Confirmado", "En preparación"].includes(o.status))
      throw new OrderError("Solo se pueden cancelar pedidos que aún no se han enviado");
    await restock(c, o.id, code, "ENTRADA_CANCELACION");
    await c.query("UPDATE orders SET status='Cancelado', payment_status='No cobrado - pedido cancelado' WHERE id=$1", [o.id]);
    await addEvent(c, o.id, "Cancelado", "Pedido cancelado. Inventario liberado en el ERP. Notificación enviada al cliente.");
  });
}

export async function returnOrder(code: string, reason: string) {
  return tx(async (c) => {
    const { rows: [o] } = await c.query<{ id: number; status: string; payment_method: string }>(
      "SELECT id,status,payment_method FROM orders WHERE code=$1 FOR UPDATE", [code],
    );
    if (!o) throw new OrderError("Pedido no encontrado");
    if (!["Entregado", "Cerrado"].includes(o.status))
      throw new OrderError("Solo se pueden devolver pedidos entregados");
    await c.query(
      "UPDATE orders SET status='Devuelto', return_reason=$1, payment_status='Revisión de devolución pendiente' WHERE id=$2",
      [reason || "Sin motivo", o.id],
    );
    await addEvent(c, o.id, "Devuelto",
      "Solicitud de devolución registrada en el ERP. El RMA requiere inspección y autorización antes de reingresar mercancía o emitir un reembolso.");
  });
}

export type OrderRow = {
  id: number; code: string; channel: Channel; customer_name: string; customer_email: string; customer_phone: string | null;
  company: string | null; tax_id: string | null; address: string; city: string; payment_method: string; payment_status: string;
  subtotal: number; tax_amount: number; shipping_charge: number; total: number; logistics_cost: number; status: string; carrier: string | null;
  tracking: string | null; promised_at: Date; shipped_at: Date | null; delivered_at: Date | null; return_reason: string | null;
  nps_score: number | null; created_at: Date;
  erp_order_id: string | null; erp_order_number: string | null; erp_status: string | null; erp_error: string | null;
};

export async function getOrder(code: string) {
  const [order] = await query<OrderRow>("SELECT * FROM orders WHERE code=$1", [code]);
  if (!order) return null;
  const items = await query<{ sku: string; name: string; qty_requested: number; qty: number; unit_price: number; line_total: number }>(
    "SELECT sku,name,qty_requested,qty,unit_price,line_total FROM order_items WHERE order_id=$1 ORDER BY id", [order.id],
  );
  const events = await query<{ status: string; note: string | null; created_at: Date }>(
    "SELECT status,note,created_at FROM order_events WHERE order_id=$1 ORDER BY id", [order.id],
  );
  return { order, items, events };
}
