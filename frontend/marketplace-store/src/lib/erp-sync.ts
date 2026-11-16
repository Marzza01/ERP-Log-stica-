import "server-only";
import { query, tx } from "./db";
import {
  erpCatalog, erpConfig, erpCreateOrder, erpGetOrder, erpTransitionOrder,
  erpCancelOrder, erpRequestReturn, erpVerifyTransfer, erpAuthorizeCredit,
  erpConfirmCashOnDelivery, ErpError,
} from "./erp";

async function log(kind: string, ok: boolean, detail: string) {
  await query("INSERT INTO erp_sync_log (kind, ok, detail) VALUES ($1,$2,$3)", [kind, ok, detail.slice(0, 2000)]);
}

function parseDimensions(value?: string) {
  const match = String(value ?? "").match(/(\d+(?:[.,]\d+)?)\s*[x×]\s*(\d+(?:[.,]\d+)?)\s*[x×]\s*(\d+(?:[.,]\d+)?)/i);
  return match ? match.slice(1).map((part) => Number(part.replace(",", "."))) : [null, null, null];
}

/** ERP → Tienda: sincronización completa del catálogo publicado con credenciales Admin. */
export async function syncCatalogFromErp() {
  const cfg = erpConfig("admin");
  if (!cfg) throw new ErpError("La sincronización completa requiere ERP_ADMIN_EMAIL y ERP_ADMIN_PASSWORD.");
  try {
    const pending = await query<{ n: number }>(
      "SELECT COUNT(*)::int AS n FROM orders WHERE status='Pendiente ERP'",
    );
    if ((pending[0]?.n ?? 0) > 0) {
      throw new ErpError("Hay " + pending[0].n + " pedidos pendientes de confirmación ERP. Reinténtalos o cancélalos antes de sincronizar el stock.");
    }

    const items = await erpCatalog(cfg);
    if (!items.length) throw new ErpError("El ERP devolvió un catálogo vacío; no se modificó la tienda.");
    const rows = items.map((p) => {
      const [length_cm, width_cm, height_cm] = parseDimensions(p.dimensionesCm);
      return {
        sku: p.sku.trim(),
        erp_id: p.id,
        name: p.nombre,
        category: p.categoriaNombre ?? "Sin categoría",
        subcategory: p.subcategoria ?? "",
        price_b2c: p.precioB2C,
        price_b2b: p.precioB2B ?? 0,
        stock: p.disponible,
        stock_min: p.stockMinimo ?? 0,
        supplier: p.proveedorNombre ?? "",
        origin_country: p.paisOrigen ?? "El Salvador",
        weight_kg: p.pesoKg ?? 0.5,
        length_cm,
        width_cm,
        height_cm,
        unit: p.unidadMedida ?? "Unidad",
        status: (p.estaPublicado ?? true) && (p.estadoProducto ?? "Activo") === "Activo" ? "Activo" : "Inactivo",
        ean: p.codigoEAN?.trim() || null,
        description: p.descripcion ?? "",
        image_url: p.imagenUrl?.trim() || null,
      };
    });
    if (new Set(rows.map((row) => row.sku)).size !== rows.length) {
      throw new ErpError("El catálogo ERP contiene SKU duplicados; no se aplicaron cambios.");
    }

    const result = await tx(async (c) => {
      const imported = await c.query<{ sku: string; inserted: boolean }>(
        `INSERT INTO products (
           sku,name,category,subcategory,price_b2c,price_b2b,stock,stock_min,supplier,origin_country,
           weight_kg,length_cm,width_cm,height_cm,unit,status,ean,description,image_url,erp_id,erp_synced_at
         )
         SELECT sku,name,category,subcategory,price_b2c,price_b2b,stock,stock_min,supplier,origin_country,
           weight_kg,length_cm,width_cm,height_cm,unit,status,ean,description,image_url,erp_id,now()
         FROM jsonb_to_recordset($1::jsonb) AS x(
           sku text,name text,category text,subcategory text,price_b2c numeric,price_b2b numeric,stock int,
           stock_min int,supplier text,origin_country text,weight_kg numeric,length_cm numeric,width_cm numeric,
           height_cm numeric,unit text,status text,ean text,description text,image_url text,erp_id text
         )
         ON CONFLICT (sku) DO UPDATE SET
           name=EXCLUDED.name,category=EXCLUDED.category,subcategory=EXCLUDED.subcategory,
           price_b2c=EXCLUDED.price_b2c,price_b2b=EXCLUDED.price_b2b,stock=EXCLUDED.stock,
           stock_min=EXCLUDED.stock_min,supplier=EXCLUDED.supplier,origin_country=EXCLUDED.origin_country,
           weight_kg=EXCLUDED.weight_kg,length_cm=EXCLUDED.length_cm,width_cm=EXCLUDED.width_cm,
           height_cm=EXCLUDED.height_cm,unit=EXCLUDED.unit,status=EXCLUDED.status,ean=EXCLUDED.ean,
           description=EXCLUDED.description,image_url=COALESCE(EXCLUDED.image_url,products.image_url),
           erp_id=EXCLUDED.erp_id,erp_synced_at=now(),updated_at=now()
         RETURNING sku,(xmax=0) AS inserted`,
        [JSON.stringify(rows)],
      );
      const presentSkus = rows.map((row) => row.sku);
      const deactivated = await c.query<{ sku: string }>(
        "UPDATE products SET status='Inactivo',erp_synced_at=now(),updated_at=now() " +
          "WHERE NOT (sku = ANY($1::text[])) AND status <> 'Inactivo' RETURNING sku",
        [presentSkus],
      );
      return {
        created: imported.rows.filter((row) => row.inserted).length,
        updated: imported.rows.filter((row) => !row.inserted).length,
        linked: imported.rows.length,
        deactivated: deactivated.rows.length,
      };
    });

    const detail = `ERP: ${items.length} productos publicados; ${result.created} nuevos, ${result.updated} actualizados y ${result.deactivated} desactivados. SKU, categorías, EAN, precios B2B/B2C y stock sincronizados con credenciales Admin.`;
    await log("catalogo", true, detail);
    return { erpProducts: items.length, ...result, withStock: items.length, detail };
  } catch (e) {
    await log("catalogo", false, (e as Error).message);
    throw e;
  }
}

/** Tienda → OMS del ERP: envía el pedido y deja el cobro pendiente de validación. */
export async function pushOrderToErp(code: string) {
  const cfg = erpConfig();
  if (!cfg) return { skipped: true as const };
  const [order] = await query<{
    id: number;
    erp_order_id: string | null;
    status: string;
    channel: "B2B" | "B2C";
    customer_name: string;
    customer_email: string;
    customer_phone: string | null;
    company: string | null;
    tax_id: string | null;
    address: string;
    city: string;
    payment_method: string;
  }>(
    "SELECT id, erp_order_id, status, channel, customer_name, customer_email, customer_phone, company, tax_id, address, city, payment_method FROM orders WHERE code = $1",
    [code],
  );
  if (!order || order.erp_order_id) return { skipped: true as const };
  if (order.status === "Cancelado" || order.status === "Devuelto")
    return { ok: false as const, error: "El pedido ya fue cancelado o devuelto y no puede enviarse al ERP." };
  const items = await query<{ sku: string; qty: number }>(
    "SELECT sku, qty FROM order_items WHERE order_id = $1 AND qty > 0 ORDER BY sku",
    [order.id],
  );
  try {
    if (!items.length) throw new ErpError("El pedido no contiene líneas con cantidad válida.");
    const created = await erpCreateOrder(cfg, {
      externalOrderCode: code,
      customerType: order.channel,
      customerName: order.customer_name,
      customerEmail: order.customer_email,
      customerPhone: order.customer_phone,
      company: order.company,
      taxId: order.tax_id,
      deliveryAddress: order.address,
      city: order.city,
      requestedPaymentMethod: order.payment_method,
      items: items.map((i) => ({ sku: i.sku, quantity: i.qty })),
    }, "LOGIMKT-" + code);

    let number: string | null = created.number;
    let status: string | null = created.status;
    try {
      const erpOrder = await erpGetOrder(cfg, created.id);
      number = erpOrder.numeroOrden;
      status = erpOrder.estado;
    } catch {}

    const updated = await query<{ status: string }>(
      `UPDATE orders
       SET erp_order_id=$1, erp_order_number=$2, erp_status=$3, erp_error=NULL,
           subtotal=COALESCE($4,subtotal), tax_amount=COALESCE($5,tax_amount),
           shipping_charge=COALESCE($6,shipping_charge), total=COALESCE($7,total),
           status=CASE WHEN status='Pendiente ERP' THEN 'Confirmado' ELSE status END
       WHERE id=$8 RETURNING status`,
      [created.id, number, status, created.subtotal, created.tax, created.shipping, created.total, order.id],
    );
    if (order.status === "Pendiente ERP" && updated[0]?.status === "Confirmado") {
      await query("INSERT INTO order_events (order_id,status,note) VALUES ($1,'Confirmado',$2)", [
        order.id,
        "Pedido registrado en el OMS del ERP como " + (number ?? created.id) +
          ". El ERP validó catálogo y reservó inventario; el cobro sigue pendiente.",
      ]);
    }
    await log("pedido", true, code + " → ERP " + (number ?? created.id));
    return { ok: true as const, erpId: created.id, number };
  } catch (e) {
    const msg = (e as Error).message;
    await query("UPDATE orders SET erp_error=$1 WHERE id=$2", [msg, order.id]);
    await log("pedido", false, code + ": " + msg);
    return { ok: false as const, error: msg };
  }
}

/** Sincroniza el estado local ya avanzado con la máquina de estados del ERP. */
export async function syncOrderStateToErp(code: string) {
  const [order] = await query<{
    id: number;
    status: string;
    erp_order_id: string | null;
    tracking: string | null;
    return_reason: string | null;
  }>(
    "SELECT id, status, erp_order_id, tracking, return_reason FROM orders WHERE code=$1",
    [code],
  );
  if (!order?.erp_order_id) return { skipped: true as const };

  const cfg = erpConfig("admin");
  if (!cfg) throw new ErpError("Para sincronizar estados configura ERP_ADMIN_EMAIL y ERP_ADMIN_PASSWORD.");

  try {
    let current = await erpGetOrder(cfg, order.erp_order_id);
    const apply = async (status: string, tracking?: string) => {
      await erpTransitionOrder(cfg, code, status, tracking);
      current = await erpGetOrder(cfg, order.erp_order_id!);
    };
    const approveAndStart = async () => {
      if (current.estado === "Pendiente") await apply("Aprobada");
      if (current.estado === "Aprobada") await apply("EnProceso");
    };
    const ship = async () => {
      await approveAndStart();
      if (current.estado === "EnProceso") {
        if (!order.tracking) throw new ErpError("El pedido local no tiene guía de envío para el ERP.");
        await apply("Enviada", order.tracking);
      }
    };

    if (order.status === "En preparación") {
      await approveAndStart();
    } else if (order.status === "Enviado") {
      await ship();
    } else if (order.status === "Entregado" || order.status === "Cerrado") {
      await ship();
      if (current.estado === "Enviada") await apply("Entregada");
    } else if (order.status === "Cancelado") {
      await erpCancelOrder(cfg, code);
      current = await erpGetOrder(cfg, order.erp_order_id);
    } else if (order.status === "Devuelto") {
      await erpRequestReturn(cfg, code, "RET-" + code, order.return_reason || "Sin motivo");
    }

    await query("UPDATE orders SET erp_status=$1, erp_error=NULL WHERE id=$2", [current.estado, order.id]);
    await log("estado", true, code + " → ERP " + current.estado);
    return { ok: true as const, status: current.estado };
  } catch (e) {
    const msg = (e as Error).message;
    await query("UPDATE orders SET erp_error=$1 WHERE id=$2", [msg, order.id]);
    await log("estado", false, code + ": " + msg);
    throw e;
  }
}

/** Cancela primero en el ERP; solo después se libera la copia de stock de la tienda. */
export async function cancelOrderInErp(code: string) {
  const [order] = await query<{ id: number; erp_order_id: string | null }>(
    "SELECT id, erp_order_id FROM orders WHERE code=$1", [code],
  );
  if (!order) return { skipped: true as const };
  const cfg = erpConfig("admin");
  if (!cfg) {
    if (!erpConfig()) return { skipped: true as const };
    throw new ErpError("Para cancelar una orden integrada configura ERP_ADMIN_EMAIL y ERP_ADMIN_PASSWORD.");
  }
  try {
    // Se cancela por código externo incluso si el POST de creación tuvo timeout
    // y la tienda aún no alcanzó a guardar el identificador ERP.
    const result = await erpCancelOrder(cfg, code) as {
      erpOrderId?: string; erpOrderNumber?: string; status?: string;
    };
    const erpId = result?.erpOrderId ?? order.erp_order_id;
    const current = erpId ? await erpGetOrder(cfg, erpId) : null;
    await query(
      "UPDATE orders SET erp_order_id=COALESCE(erp_order_id,$1), erp_order_number=COALESCE($2,erp_order_number), erp_status=$3, erp_error=NULL WHERE id=$4",
      [erpId ?? null, result?.erpOrderNumber ?? null, current?.estado ?? result?.status ?? "Cancelada", order.id],
    );
    await log("cancelacion", true, code + " → ERP " + (current?.estado ?? result?.status ?? "Cancelada"));
    return { ok: true as const };
  } catch (e) {
    if (e instanceof ErpError && e.status === 404 && !order.erp_order_id) {
      await log("cancelacion", true, code + " → no existía orden remota; se cancelará solo en la tienda");
      return { skipped: true as const };
    }
    const msg = (e as Error).message;
    await query("UPDATE orders SET erp_error=$1 WHERE id=$2", [msg, order.id]);
    await log("cancelacion", false, code + ": " + msg);
    throw e;
  }
}

/** Registra primero el RMA en el ERP. El inventario no se reingresa hasta inspección. */
export async function requestReturnInErp(code: string, reason: string) {
  const [order] = await query<{ id: number; erp_order_id: string | null }>(
    "SELECT id, erp_order_id FROM orders WHERE code=$1", [code],
  );
  if (!order?.erp_order_id) return { skipped: true as const };
  const cfg = erpConfig("admin");
  if (!cfg) throw new ErpError("Para registrar devoluciones configura ERP_ADMIN_EMAIL y ERP_ADMIN_PASSWORD.");
  try {
    await erpRequestReturn(cfg, code, "RET-" + code, reason);
    await query("UPDATE orders SET erp_error=NULL WHERE id=$1", [order.id]);
    await log("devolucion", true, code + " → RMA solicitada en ERP");
    return { ok: true as const };
  } catch (e) {
    const msg = (e as Error).message;
    await query("UPDATE orders SET erp_error=$1 WHERE id=$2", [msg, order.id]);
    await log("devolucion", false, code + ": " + msg);
    throw e;
  }
}

export async function verifyTransferInErp(code: string) {
  return verifyPaymentInErp(code, erpVerifyTransfer, "transferencia");
}

export async function authorizeCreditInErp(code: string) {
  return verifyPaymentInErp(code, erpAuthorizeCredit, "credito_30");
}

export async function confirmCashOnDeliveryInErp(code: string) {
  return verifyPaymentInErp(code, erpConfirmCashOnDelivery, "contra_entrega");
}

async function verifyPaymentInErp(
  code: string,
  operation: (cfg: NonNullable<ReturnType<typeof erpConfig>>, code: string) => Promise<{ paymentStatus?: string }>,
  kind: string,
) {
  const [order] = await query<{ id: number; erp_order_id: string | null }>(
    "SELECT id, erp_order_id FROM orders WHERE code=$1", [code],
  );
  if (!order?.erp_order_id) throw new ErpError("El pedido todavía no está vinculado al ERP.");
  const cfg = erpConfig("admin");
  if (!cfg) throw new ErpError("Para validar pagos configura ERP_ADMIN_EMAIL y ERP_ADMIN_PASSWORD.");
  const result = await operation(cfg, code);
  const status = result.paymentStatus;
  if (status) await query("UPDATE orders SET payment_status=$1 WHERE id=$2", [status, order.id]);
  await log("pago_" + kind, true, code + ": " + (status ?? "verificado"));
  return { ok: true as const, paymentStatus: status };
}

/** Consulta el estado actual del pedido en el ERP (para la página de seguimiento). */
export async function refreshErpOrder(code: string) {
  const cfg = erpConfig();
  if (!cfg) return null;
  const [o] = await query<{ id: number; erp_order_id: string | null }>(
    "SELECT id, erp_order_id FROM orders WHERE code=$1", [code],
  );
  if (!o?.erp_order_id) return null;
  try {
    const r = await erpGetOrder(cfg, o.erp_order_id);
    await query("UPDATE orders SET erp_status=$1, erp_order_number=$2 WHERE id=$3", [r.estado, r.numeroOrden, o.id]);
    return r;
  } catch {
    return null;
  }
}
