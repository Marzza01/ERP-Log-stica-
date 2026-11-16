"use server";

import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { revalidatePath } from "next/cache";
import { query } from "@/lib/db";
import { checkPassword, isAdmin, sessionValue } from "@/lib/session";
import { advanceOrder, cancelOrder, OrderError, returnOrder } from "@/lib/orders";
import {
  authorizeCreditInErp, cancelOrderInErp, confirmCashOnDeliveryInErp,
  requestReturnInErp, syncOrderStateToErp, verifyTransferInErp,
} from "@/lib/erp-sync";
import { ErpError } from "@/lib/erp";

/* ---------- Tienda ---------- */

export async function setMode(formData: FormData) {
  const mode = formData.get("modo") === "B2B" ? "B2B" : "B2C";
  const c = await cookies();
  c.set("modo", mode, { path: "/", maxAge: 60 * 60 * 24 * 30, sameSite: "lax" });
  const back = String(formData.get("back") || "/");
  redirect(back.startsWith("/") ? back : "/");
}

export async function addReview(formData: FormData) {
  const sku = String(formData.get("sku") || "");
  const author = String(formData.get("author") || "").trim().slice(0, 60);
  const rating = Math.round(Number(formData.get("rating")));
  const comment = String(formData.get("comment") || "").trim().slice(0, 1000);
  if (!sku || !author || !(rating >= 1 && rating <= 5)) redirect(`/producto/${encodeURIComponent(sku)}?resena=error#resenas`);
  await query("INSERT INTO reviews (sku, author, rating, comment) VALUES ($1,$2,$3,$4)", [sku, author, rating, comment || null]);
  revalidatePath(`/producto/${sku}`);
  redirect(`/producto/${encodeURIComponent(sku)}?resena=ok#resenas`);
}

export async function submitNps(formData: FormData) {
  const code = String(formData.get("code") || "");
  const score = Math.round(Number(formData.get("score")));
  const comment = String(formData.get("comment") || "").trim().slice(0, 1000);
  if (score >= 0 && score <= 10) {
    await query(
      "UPDATE orders SET nps_score=$1, nps_comment=$2 WHERE code=$3 AND status IN ('Entregado','Cerrado','Devuelto')",
      [score, comment || null, code],
    );
  }
  redirect(`/pedido/${encodeURIComponent(code)}?encuesta=ok`);
}

export async function findOrder(formData: FormData) {
  const code = String(formData.get("code") || "").trim().toUpperCase();
  redirect(code ? `/pedido/${encodeURIComponent(code)}` : "/pedido");
}

/* ---------- Administración (ERP) ---------- */

export async function adminLogin(formData: FormData) {
  if (!process.env.ADMIN_PASSWORD && process.env.VERCEL) redirect("/admin?error=config");
  if (!checkPassword(String(formData.get("password") || ""))) redirect("/admin?error=1");
  const c = await cookies();
  c.set("admin", sessionValue(), { path: "/", httpOnly: true, sameSite: "lax", secure: process.env.NODE_ENV === "production", maxAge: 60 * 60 * 12 });
  redirect("/admin");
}

export async function adminLogout() {
  const c = await cookies();
  c.delete("admin");
  redirect("/admin");
}

async function guard() {
  if (!(await isAdmin())) redirect("/admin");
}

function backWith(formData: FormData, msg: string, ok: boolean): never {
  const back = String(formData.get("back") || "/admin/pedidos");
  const url = new URL(back, "http://x");
  url.searchParams.set(ok ? "ok" : "error", msg);
  redirect(url.pathname + url.search);
}

async function orderAction(formData: FormData, fn: (code: string) => Promise<unknown>, okMsg: (code: string) => string) {
  await guard();
  const code = String(formData.get("code") || "");
  try {
    await fn(code);
  } catch (e) {
    if (e instanceof OrderError || e instanceof ErpError) backWith(formData, e.message, false);
    throw e;
  }
  revalidatePath("/admin", "layout");
  backWith(formData, okMsg(code), true);
}

export async function advanceOrderAction(formData: FormData) {
  await orderAction(formData, async (code) => {
    await advanceOrder(code);
    await syncOrderStateToErp(code);
  }, (c) => `Pedido ${c} actualizado en la tienda y sincronizado con el ERP`);
}

export async function cancelOrderAction(formData: FormData) {
  await orderAction(formData, async (code) => {
    await cancelOrderInErp(code);
    await cancelOrder(code);
  }, (c) => `Pedido ${c} cancelado y stock local liberado`);
}

export async function returnOrderAction(formData: FormData) {
  const reason = String(formData.get("reason") || "Producto defectuoso");
  await orderAction(formData, async (code) => {
    await syncOrderStateToErp(code);
    await requestReturnInErp(code, reason);
    await returnOrder(code, reason);
  }, (c) => `Devolución del pedido ${c} registrada en el ERP; pendiente de inspección`);
}

export async function verifyTransferAction(formData: FormData) {
  await orderAction(formData, verifyTransferInErp, (c) => `Transferencia del pedido ${c} verificada manualmente`);
}

export async function authorizeCreditAction(formData: FormData) {
  await orderAction(formData, authorizeCreditInErp, (c) => `Crédito del pedido ${c} autorizado manualmente`);
}

export async function confirmCashOnDeliveryAction(formData: FormData) {
  await orderAction(formData, confirmCashOnDeliveryInErp, (c) => `Cobro contra entrega del pedido ${c} confirmado manualmente`);
}

/** Recepción de mercancía o ajuste manual de inventario. */
export async function adjustStock(formData: FormData) {
  await guard();
  const sku = String(formData.get("sku") || "");
  const qty = Math.round(Number(formData.get("qty")));
  const type = formData.get("type") === "AJUSTE" ? "AJUSTE" : "ENTRADA_COMPRA";
  const ref = String(formData.get("ref") || "").trim() || (type === "AJUSTE" ? "Ajuste manual" : "Recepción de mercancía");
  if (!sku || !Number.isFinite(qty) || qty === 0) backWith(formData, "Indica una cantidad distinta de cero", false);
  const rows = await query<{ stock: number }>(
    "UPDATE products SET stock = stock + $1, updated_at = now() WHERE sku = $2 AND stock + $1 >= 0 RETURNING stock",
    [qty, sku],
  );
  if (!rows.length) backWith(formData, "No se pudo ajustar: el stock no puede quedar negativo", false);
  await query("INSERT INTO inventory_movements (sku,type,qty,reference) VALUES ($1,$2,$3,$4)", [sku, type, qty, ref]);
  revalidatePath("/admin", "layout");
  backWith(formData, `Stock de ${sku} actualizado a ${rows[0].stock}`, true);
}

/* ---------- Integración ERP ---------- */

export async function erpTestAction(formData: FormData) {
  await guard();
  const { erpConfig, erpPing } = await import("@/lib/erp");
  const cfg = erpConfig();
  if (!cfg) backWith(formData, "Falta configurar ERP_API_URL, ERP_EMAIL y ERP_PASSWORD", false);
  let msg: string;
  try {
    const r = await erpPing(cfg!);
    msg = `Conexión correcta con ${cfg!.email} (rol ${r.role || "desconocido"}). El ERP tiene ${r.products} productos publicados.`;
    const adminCfg = erpConfig("catalogo");
    if (adminCfg && adminCfg.email !== cfg!.email) {
      const a = await erpPing(adminCfg);
      msg += ` Usuario de catálogo ${adminCfg.email} (rol ${a.role || "desconocido"}) también conectado.`;
    }
  } catch (e) {
    backWith(formData, (e as Error).message, false);
  }
  backWith(formData, msg!, true);
}

export async function erpSyncAction(formData: FormData) {
  await guard();
  const { syncCatalogFromErp } = await import("@/lib/erp-sync");
  let msg: string;
  try {
    msg = (await syncCatalogFromErp()).detail;
  } catch (e) {
    backWith(formData, (e as Error).message, false);
  }
  revalidatePath("/", "layout");
  backWith(formData, msg!, true);
}

export async function erpRetryAction(formData: FormData) {
  await guard();
  const { pushOrderToErp, syncOrderStateToErp } = await import("@/lib/erp-sync");
  const code = String(formData.get("code") || "");
  try {
    const [order] = await query<{ erp_order_id: string | null }>(
      "SELECT erp_order_id FROM orders WHERE code=$1", [code],
    );
    const result = order?.erp_order_id
      ? await syncOrderStateToErp(code)
      : await pushOrderToErp(code);
    revalidatePath("/admin", "layout");
    if ("ok" in result && result.ok === false) backWith(formData, result.error ?? "Error al sincronizar", false);
    backWith(formData, `Pedido ${code} sincronizado con el ERP`, true);
  } catch (e) {
    backWith(formData, e instanceof Error ? e.message : "Error al sincronizar con el ERP", false);
  }
}
