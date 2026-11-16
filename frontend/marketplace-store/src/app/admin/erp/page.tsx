import Link from "next/link";
import { isAdmin } from "@/lib/session";
import { query } from "@/lib/db";
import { erpConfig } from "@/lib/erp";
import { erpRetryAction, erpSyncAction, erpTestAction } from "@/app/actions";

export default async function ErpPage({ searchParams }: PageProps<"/admin/erp">) {
  if (!(await isAdmin())) return null;
  const sp = await searchParams;
  const cfg = erpConfig();
  const [[linked], logs, orders] = await Promise.all([
    query<{ n: number; total: number; last: Date | null }>(
      "SELECT (count(*) FILTER (WHERE erp_id IS NOT NULL))::int AS n, count(*)::int AS total, max(erp_synced_at) AS last FROM products",
    ),
    query<{ kind: string; ok: boolean; detail: string; created_at: Date }>(
      "SELECT kind, ok, detail, created_at FROM erp_sync_log ORDER BY id DESC LIMIT 15",
    ),
    query<{ code: string; erp_order_number: string | null; erp_status: string | null; erp_error: string | null; status: string }>(
      "SELECT code, erp_order_number, erp_status, erp_error, status FROM orders ORDER BY id DESC LIMIT 30",
    ),
  ]);
  const back = "/admin/erp";
  const when = (d: Date | null) => (d ? new Date(d).toLocaleString("es-SV", { timeZone: "America/El_Salvador" }) : "nunca");

  return (
    <div className="space-y-6">
      <div>
        <h1 className="ancho text-2xl font-bold">Conexión con el ERP</h1>
        <p className="max-w-3xl text-sm text-gris">
          La tienda trae del ERP los productos, precios y stock (por SKU) y le envía cada pedido nuevo a su OMS, que reserva el
          inventario y lleva el estado del pedido.
        </p>
      </div>
      {typeof sp.ok === "string" && <p className="rounded-md border-2 border-ok bg-white p-3 text-sm">{sp.ok}</p>}
      {typeof sp.error === "string" && <p className="rounded-md border-2 border-alerta bg-white p-3 text-sm text-alerta">{sp.error}</p>}

      <section className="grid gap-4 md:grid-cols-3">
        <div className="rounded-lg border border-linea bg-white p-5">
          <p className="text-sm text-gris">Estado</p>
          <p className={`mt-1 text-lg font-bold ${cfg ? "text-ok" : "text-alerta"}`}>{cfg ? "Configurada" : "Sin configurar"}</p>
          <p className="mt-1 break-all text-xs text-gris">{cfg ? `${cfg.url} con el usuario ${cfg.email}` : "Agrega ERP_API_URL, ERP_EMAIL y ERP_PASSWORD."}</p>
        </div>
        <div className="rounded-lg border border-linea bg-white p-5">
          <p className="text-sm text-gris">Productos vinculados al ERP</p>
          <p className="ancho mt-1 text-2xl font-bold">{linked.n} de {linked.total}</p>
          <p className="mt-1 text-xs text-gris">Última sincronización: {when(linked.last)}</p>
        </div>
        <div className="flex flex-col gap-2 rounded-lg border border-linea bg-white p-5">
          <form action={erpTestAction}>
            <input type="hidden" name="back" value={back} />
            <button disabled={!cfg} className="w-full rounded-md border border-azul px-4 py-2 text-sm font-semibold text-azul hover:bg-azul-claro disabled:opacity-40">
              Probar conexión
            </button>
          </form>
          <form action={erpSyncAction}>
            <input type="hidden" name="back" value={back} />
            <button disabled={!cfg} className="w-full rounded-md bg-azul px-4 py-2 text-sm font-semibold text-white hover:bg-azul-oscuro disabled:opacity-40">
              Sincronizar catálogo y stock
            </button>
          </form>
          <p className="text-xs text-gris">La primera vez puede tardar hasta un minuto si el servidor del ERP estaba dormido.</p>
        </div>
      </section>

      <section className="rounded-lg border border-linea bg-white p-5">
        <h2 className="font-semibold">Pedidos enviados al OMS del ERP</h2>
        <table className="mt-3 w-full text-sm">
          <thead className="text-left text-xs text-gris">
            <tr><th className="py-2">Pedido tienda</th><th className="py-2">Orden en ERP</th><th className="py-2">Estado en ERP</th><th className="py-2">Resultado</th><th /></tr>
          </thead>
          <tbody>
            {orders.length === 0 && <tr><td colSpan={5} className="py-4 text-gris">Aún no hay pedidos.</td></tr>}
            {orders.map((o) => (
              <tr key={o.code} className="border-t border-linea align-top">
                <td className="py-2"><Link href={`/pedido/${o.code}`} className="font-medium text-azul hover:underline">{o.code}</Link></td>
                <td className="py-2">{o.erp_order_number ?? "—"}</td>
                <td className="py-2">{o.erp_status ?? "—"}</td>
                <td className="py-2 text-xs">
                  {o.erp_order_number ? <span className="text-ok">Registrado</span> : o.erp_error ? <span className="text-alerta">{o.erp_error}</span> : <span className="text-gris">Pendiente</span>}
                </td>
                <td className="py-2 text-right">
                  {!o.erp_order_number && cfg && o.status !== "Cancelado" && (
                    <form action={erpRetryAction}>
                      <input type="hidden" name="code" value={o.code} />
                      <input type="hidden" name="back" value={back} />
                      <button className="rounded-md border border-linea px-2 py-1 text-xs hover:bg-papel">Enviar al ERP</button>
                    </form>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>

      <section className="rounded-lg border border-linea bg-white p-5">
        <h2 className="font-semibold">Registro de la conexión</h2>
        <ul className="mt-2 space-y-1 text-sm">
          {logs.length === 0 && <li className="text-gris">Sin actividad todavía.</li>}
          {logs.map((l, i) => (
            <li key={i} className="flex gap-3">
              <span className={l.ok ? "text-ok" : "text-alerta"}>{l.ok ? "Correcto" : "Error"}</span>
              <span className="text-gris">{when(l.created_at)}</span>
              <span>{l.detail}</span>
            </li>
          ))}
        </ul>
      </section>
    </div>
  );
}
