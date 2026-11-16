import Link from "next/link";
import { isAdmin } from "@/lib/session";
import { query } from "@/lib/db";
import { nextStatus, type OrderRow } from "@/lib/orders";
import { RETURN_REASONS, money, statusColor } from "@/lib/shared";
import {
  advanceOrderAction, authorizeCreditAction, cancelOrderAction, confirmCashOnDeliveryAction,
  erpRetryAction, returnOrderAction, verifyTransferAction,
} from "@/app/actions";

const STATES = ["", "Pendiente ERP", "Confirmado", "En preparación", "Enviado", "Entregado", "Cerrado", "Devuelto", "Cancelado"];

export default async function PedidosAdmin({ searchParams }: PageProps<"/admin/pedidos">) {
  if (!(await isAdmin())) return null;
  const sp = await searchParams;
  const estado = typeof sp.estado === "string" ? sp.estado : "";
  const canal = typeof sp.canal === "string" ? sp.canal : "";
  const where: string[] = [];
  const params: string[] = [];
  if (estado) { params.push(estado); where.push(`status = $${params.length}`); }
  if (canal) { params.push(canal); where.push(`channel = $${params.length}`); }
  const orders = await query<OrderRow & { items: number }>(
    `SELECT o.*, (SELECT sum(qty) FROM order_items WHERE order_id = o.id)::int AS items
     FROM orders o ${where.length ? "WHERE " + where.join(" AND ") : ""} ORDER BY id DESC LIMIT 200`,
    params,
  );
  const back = `/admin/pedidos${estado || canal ? "?" + new URLSearchParams({ ...(estado && { estado }), ...(canal && { canal }) }) : ""}`;

  return (
    <div>
      <h1 className="ancho text-2xl font-bold">Gestión de pedidos (OMS)</h1>
      <p className="text-sm text-gris">
        Flujo: Recibido → Confirmado → En preparación → Enviado → Entregado → Cerrado. Cada cambio notifica al cliente y alimenta los KPIs.
      </p>
      {typeof sp.ok === "string" && <p className="mt-4 rounded-lg bg-emerald-50 p-3 text-sm text-emerald-800">{sp.ok}</p>}
      {typeof sp.error === "string" && <p className="mt-4 rounded-lg bg-red-50 p-3 text-sm text-red-700">{sp.error}</p>}

      <form className="mt-4 flex flex-wrap gap-2 text-sm">
        <select name="estado" defaultValue={estado} className="rounded-lg border border-linea px-3 py-2">
          {STATES.map((s) => <option key={s} value={s}>{s || "Todos los estados"}</option>)}
        </select>
        <select name="canal" defaultValue={canal} className="rounded-lg border border-linea px-3 py-2">
          <option value="">B2C y B2B</option>
          <option value="B2C">B2C</option>
          <option value="B2B">B2B</option>
        </select>
        <button className="rounded-lg bg-tinta px-4 py-2 font-semibold text-white">Filtrar</button>
      </form>

      <div className="mt-4 overflow-x-auto rounded-lg border border-linea bg-white">
        <table className="w-full text-sm">
          <thead className="bg-papel text-left text-xs uppercase text-gris">
            <tr>
              <th className="p-3">Pedido</th><th className="p-3">Cliente</th><th className="p-3">Canal</th><th className="p-3">Unid.</th>
              <th className="p-3">Total</th><th className="p-3">Pago</th><th className="p-3">Estado</th><th className="p-3">Acciones</th>
            </tr>
          </thead>
          <tbody>
            {orders.length === 0 && (
              <tr><td colSpan={8} className="p-6 text-center text-gris">No hay pedidos.</td></tr>
            )}
            {orders.map((o) => {
              const next = nextStatus(o.status);
              return (
                <tr key={o.id} className="border-t align-top">
                  <td className="p-3">
                    <Link href={`/pedido/${o.code}`} className="font-semibold text-azul hover:underline">{o.code}</Link>
                    <p className="text-xs text-gris">{new Date(o.created_at).toLocaleString("es-SV", { timeZone: "America/El_Salvador" })}</p>
                  </td>
                  <td className="p-3">
                    {o.company && <p className="font-medium">{o.company}</p>}
                    <p>{o.customer_name}</p>
                    <p className="text-xs text-gris">{o.city}</p>
                  </td>
                  <td className="p-3">{o.channel}</td>
                  <td className="p-3">{o.items}</td>
                  <td className="p-3 font-medium">{money(o.total)}</td>
                  <td className="p-3 text-xs">
                    {o.payment_status}
                    {o.erp_order_number && <p className="mt-1 text-[11px] text-gris">ERP: {o.erp_order_number}</p>}
                    {o.erp_error && <p className="mt-1 max-w-56 text-[11px] text-red-700">ERP: {o.erp_error}</p>}
                  </td>
                  <td className="p-3"><span className={`whitespace-nowrap rounded-full px-2.5 py-0.5 text-xs font-semibold ${statusColor(o.status)}`}>{o.status}</span></td>
                  <td className="p-3">
                    <div className="flex flex-col gap-1.5">
                      {next && (
                        <form action={advanceOrderAction}>
                          <input type="hidden" name="code" value={o.code} />
                          <input type="hidden" name="back" value={back} />
                          <button className="w-full whitespace-nowrap rounded-lg bg-azul px-3 py-1.5 text-xs font-semibold text-white hover:bg-azul-oscuro">
                            Pasar a “{next}”
                          </button>
                        </form>
                      )}
                      {["Pendiente ERP", "Confirmado", "En preparación"].includes(o.status) && (
                        <form action={cancelOrderAction}>
                          <input type="hidden" name="code" value={o.code} />
                          <input type="hidden" name="back" value={back} />
                          <button className="w-full rounded-lg border border-red-300 px-3 py-1.5 text-xs text-red-700 hover:bg-red-50">Cancelar</button>
                        </form>
                      )}
                      {(o.status === "Pendiente ERP" || o.erp_error) && (
                        <form action={erpRetryAction}>
                          <input type="hidden" name="code" value={o.code} />
                          <input type="hidden" name="back" value={back} />
                          <button className="w-full rounded-lg border border-amber-300 px-3 py-1.5 text-xs text-amber-800 hover:bg-amber-50">Reintentar ERP</button>
                        </form>
                      )}
                      {o.erp_order_id && o.payment_method === "transferencia" && o.payment_status === "Pendiente de verificación" && (
                        <form action={verifyTransferAction}>
                          <input type="hidden" name="code" value={o.code} />
                          <input type="hidden" name="back" value={back} />
                          <button className="w-full rounded-lg border border-blue-300 px-3 py-1.5 text-xs text-blue-800 hover:bg-blue-50">Verificar transferencia</button>
                        </form>
                      )}
                      {o.erp_order_id && o.payment_method === "credito_30" && o.payment_status === "Crédito 30 días pendiente de autorización" && (
                        <form action={authorizeCreditAction}>
                          <input type="hidden" name="code" value={o.code} />
                          <input type="hidden" name="back" value={back} />
                          <button className="w-full rounded-lg border border-blue-300 px-3 py-1.5 text-xs text-blue-800 hover:bg-blue-50">Autorizar crédito</button>
                        </form>
                      )}
                      {o.erp_order_id && o.payment_method === "contra_entrega" && o.status === "Entregado" && o.payment_status === "Cobro contra entrega pendiente" && (
                        <form action={confirmCashOnDeliveryAction}>
                          <input type="hidden" name="code" value={o.code} />
                          <input type="hidden" name="back" value={back} />
                          <button className="w-full rounded-lg border border-blue-300 px-3 py-1.5 text-xs text-blue-800 hover:bg-blue-50">Confirmar cobro</button>
                        </form>
                      )}
                      {["Entregado", "Cerrado"].includes(o.status) && (
                        <form action={returnOrderAction} className="flex gap-1">
                          <input type="hidden" name="code" value={o.code} />
                          <input type="hidden" name="back" value={back} />
                          <select name="reason" className="rounded-lg border border-linea px-1 py-1 text-xs">
                            {RETURN_REASONS.map((r) => <option key={r}>{r}</option>)}
                          </select>
                          <button className="rounded-lg border border-orange-300 px-2 py-1 text-xs text-orange-700 hover:bg-orange-50">Devolver</button>
                        </form>
                      )}
                    </div>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}
