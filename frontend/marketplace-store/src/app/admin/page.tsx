import Link from "next/link";
import { isAdmin } from "@/lib/session";
import { getKpis, type Light } from "@/lib/kpis";
import { query } from "@/lib/db";
import { money, statusColor } from "@/lib/shared";

const LIGHT: Record<Light, { dot: string; label: string; ring: string }> = {
  verde: { dot: "bg-emerald-500", label: "Óptimo", ring: "border-emerald-200" },
  amarillo: { dot: "bg-amber-400", label: "Atención", ring: "border-amber-200" },
  rojo: { dot: "bg-red-500", label: "Crítico", ring: "border-red-200" },
  "sin-datos": { dot: "bg-linea", label: "Sin datos", ring: "border-linea" },
};

export default async function Dashboard() {
  if (!(await isAdmin())) return null;
  const [{ kpis, summary }, byStatus, byCategory, lowStock, recent] = await Promise.all([
    getKpis(),
    query<{ status: string; n: number }>("SELECT status, count(*)::int AS n FROM orders GROUP BY status ORDER BY n DESC"),
    query<{ category: string; units: number; revenue: number }>(`
      SELECT p.category, sum(i.qty)::int AS units, sum(i.line_total)::float AS revenue
      FROM order_items i JOIN orders o ON o.id = i.order_id JOIN products p ON p.sku = i.sku
      WHERE o.status NOT IN ('Cancelado','Devuelto') GROUP BY p.category ORDER BY revenue DESC`),
    query<{ n: number }>("SELECT count(*)::int AS n FROM products WHERE status='Activo' AND stock <= stock_min"),
    query<{ code: string; customer_name: string; channel: string; total: number; status: string }>(
      "SELECT code, customer_name, channel, total, status FROM orders ORDER BY id DESC LIMIT 6",
    ),
  ]);
  const maxRev = Math.max(1, ...byCategory.map((c) => c.revenue));

  return (
    <div className="space-y-8">
      <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
        {[
          ["Pedidos", String(summary.orders), `${summary.b2c} B2C · ${summary.b2b} B2B`],
          ["Ventas netas", money(summary.revenue), "Sin cancelados ni devueltos"],
          ["Ticket promedio", summary.ticket ? money(summary.ticket) : "—", `B2C ${summary.ticketB2C ? money(summary.ticketB2C) : "—"} · B2B ${summary.ticketB2B ? money(summary.ticketB2B) : "—"}`],
          ["Alertas de stock", String(lowStock[0].n), "SKUs en o bajo stock mínimo"],
        ].map(([t, v, d]) => (
          <div key={t} className="rounded-lg border border-linea bg-white p-5">
            <p className="text-sm text-gris">{t}</p>
            <p className="mt-1 ancho text-2xl font-bold">{v}</p>
            <p className="mt-1 text-xs text-gris">{d}</p>
          </div>
        ))}
      </div>

      <section>
        <h2 className="text-xl font-bold">Los 8 KPIs de logística digital</h2>
        <p className="text-sm text-gris">Se calculan automáticamente con los pedidos reales del sistema. Semáforo: 🟢 óptimo · 🟡 atención · 🔴 crítico.</p>
        <div className="mt-4 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {kpis.map((k) => (
            <div key={k.id} className={`rounded-lg border-2 bg-white p-5 shadow-sm ${LIGHT[k.light].ring}`}>
              <div className="flex items-center justify-between">
                <p className="text-sm font-semibold text-tinta">{k.name}</p>
                <span className={`size-3 rounded-full ${LIGHT[k.light].dot}`} title={LIGHT[k.light].label} />
              </div>
              <p className="mt-2 ancho text-2xl font-bold">{k.display}</p>
              <p className="mt-1 text-xs text-gris">Meta: {k.goal} · SCOR: {k.scor}</p>
              <p className="mt-2 text-xs text-gris">{k.formula}</p>
            </div>
          ))}
        </div>
      </section>

      <div className="grid gap-6 lg:grid-cols-3">
        <section className="rounded-lg border border-linea bg-white p-5 lg:col-span-2">
          <h2 className="font-semibold">Ventas por categoría</h2>
          {byCategory.length === 0 ? (
            <p className="mt-3 text-sm text-gris">Aún no hay ventas.</p>
          ) : (
            <ul className="mt-4 space-y-3">
              {byCategory.map((c) => (
                <li key={c.category} className="text-sm">
                  <div className="flex justify-between">
                    <span>{c.category}</span>
                    <span className="font-medium">{money(c.revenue)} · {c.units} u.</span>
                  </div>
                  <div className="mt-1 h-2 rounded-full bg-papel">
                    <div className="h-2 rounded-full bg-azul" style={{ width: `${(c.revenue / maxRev) * 100}%` }} />
                  </div>
                </li>
              ))}
            </ul>
          )}
        </section>
        <section className="rounded-lg border border-linea bg-white p-5">
          <h2 className="font-semibold">Pedidos por estado</h2>
          <ul className="mt-3 space-y-2 text-sm">
            {byStatus.length === 0 && <li className="text-gris">Sin pedidos todavía.</li>}
            {byStatus.map((s) => (
              <li key={s.status} className="flex items-center justify-between">
                <span className={`rounded-full px-2.5 py-0.5 text-xs font-semibold ${statusColor(s.status)}`}>{s.status}</span>
                <span className="font-semibold">{s.n}</span>
              </li>
            ))}
          </ul>
        </section>
      </div>

      <section className="rounded-lg border border-linea bg-white p-5">
        <div className="flex items-center justify-between">
          <h2 className="font-semibold">Últimos pedidos</h2>
          <Link href="/admin/pedidos" className="text-sm text-azul hover:underline">Gestionar pedidos →</Link>
        </div>
        <table className="mt-3 w-full text-sm">
          <tbody>
            {recent.map((o) => (
              <tr key={o.code} className="border-b last:border-0">
                <td className="py-2 font-medium"><Link href={`/pedido/${o.code}`} className="hover:text-azul">{o.code}</Link></td>
                <td className="py-2">{o.customer_name}</td>
                <td className="py-2">{o.channel}</td>
                <td className="py-2 text-right">{money(o.total)}</td>
                <td className="py-2 text-right"><span className={`rounded-full px-2.5 py-0.5 text-xs font-semibold ${statusColor(o.status)}`}>{o.status}</span></td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>
    </div>
  );
}
