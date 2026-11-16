import Link from "next/link";
import { isAdmin } from "@/lib/session";
import { query } from "@/lib/db";
import { money } from "@/lib/shared";
import { adjustStock } from "@/app/actions";
import { ImportForm } from "./import-form";

type Row = {
  sku: string; name: string; category: string; subcategory: string | null; stock: number; stock_min: number;
  price_b2c: number; price_b2b: number; supplier: string | null; status: string;
};

export default async function Inventario({ searchParams }: PageProps<"/admin/inventario">) {
  if (!(await isAdmin())) return null;
  const sp = await searchParams;
  const q = typeof sp.q === "string" ? sp.q.trim() : "";
  const alerta = sp.alerta === "1";
  const page = Math.max(1, Number(sp.page) || 1);
  const where: string[] = [];
  const params: unknown[] = [];
  if (q) { params.push(`%${q}%`); where.push(`(name ILIKE $${params.length} OR sku ILIKE $${params.length} OR supplier ILIKE $${params.length})`); }
  if (alerta) where.push("stock <= stock_min AND status = 'Activo'");
  const w = where.length ? "WHERE " + where.join(" AND ") : "";
  const [rows, [count], totals, byCat, moves] = await Promise.all([
    query<Row>(`SELECT sku,name,category,subcategory,stock,stock_min,price_b2c,price_b2b,supplier,status FROM products ${w}
                ORDER BY (stock <= stock_min) DESC, sku LIMIT 50 OFFSET ${(page - 1) * 50}`, params),
    query<{ n: number }>(`SELECT count(*)::int AS n FROM products ${w}`, params),
    query<{ skus: number; active: number; units: number; value_b2b: number; alerts: number; out: number }>(`
      SELECT count(*)::int AS skus, (count(*) FILTER (WHERE status='Activo'))::int AS active, sum(stock)::int AS units,
             sum(stock * price_b2b)::float AS value_b2b,
             (count(*) FILTER (WHERE stock <= stock_min AND status='Activo'))::int AS alerts,
             (count(*) FILTER (WHERE stock = 0))::int AS out
      FROM products`),
    query<{ category: string; skus: number; units: number }>(
      "SELECT category, count(*)::int AS skus, sum(stock)::int AS units FROM products GROUP BY category ORDER BY skus DESC",
    ),
    query<{ sku: string; type: string; qty: number; reference: string | null; created_at: Date }>(
      "SELECT sku,type,qty,reference,created_at FROM inventory_movements WHERE type <> 'CARGA_INICIAL' ORDER BY id DESC LIMIT 12",
    ),
  ]);
  const t = totals[0];
  const pages = Math.max(1, Math.ceil(count.n / 50));
  const qs = (p: number) => `/admin/inventario?${new URLSearchParams({ ...(q && { q }), ...(alerta && { alerta: "1" }), page: String(p) })}`;
  const back = qs(page);

  return (
    <div className="space-y-6">
      <div>
        <h1 className="ancho text-2xl font-bold">Inventario (ERP)</h1>
        <p className="text-sm text-gris">Stock en tiempo real: se descuenta con cada venta y se repone con devoluciones, cancelaciones y recepciones.</p>
      </div>
      {typeof sp.ok === "string" && <p className="rounded-lg bg-emerald-50 p-3 text-sm text-emerald-800">{sp.ok}</p>}
      {typeof sp.error === "string" && <p className="rounded-lg bg-red-50 p-3 text-sm text-red-700">{sp.error}</p>}

      <div className="grid grid-cols-2 gap-4 md:grid-cols-5">
        {[
          ["SKUs en catálogo", `${t.skus}`, `${t.active} activos`],
          ["Unidades en stock", t.units?.toLocaleString("es-SV") ?? "0", "Todas las bodegas"],
          ["Valor del inventario", money(t.value_b2b), "A precio B2B"],
          ["Alertas de stock mínimo", `${t.alerts}`, "Requieren reposición"],
          ["Agotados", `${t.out}`, "Stock en cero"],
        ].map(([a, b, c]) => (
          <div key={a} className="rounded-lg border border-linea bg-white p-4">
            <p className="text-xs text-gris">{a}</p>
            <p className="mt-1 text-xl font-bold">{b}</p>
            <p className="text-xs text-gris">{c}</p>
          </div>
        ))}
      </div>

      <div className="grid gap-6 lg:grid-cols-[1fr_320px]">
        <section>
          <form className="flex flex-wrap gap-2 text-sm">
            <input name="q" defaultValue={q} placeholder="Buscar por nombre, SKU o proveedor" className="flex-1 rounded-lg border border-linea px-3 py-2" />
            <label className="flex items-center gap-2 rounded-lg border border-linea bg-white px-3">
              <input type="checkbox" name="alerta" value="1" defaultChecked={alerta} /> Solo alertas
            </label>
            <button className="rounded-lg bg-tinta px-4 py-2 font-semibold text-white">Filtrar</button>
          </form>
          <div className="mt-3 overflow-x-auto rounded-lg border border-linea bg-white">
            <table className="w-full text-sm">
              <thead className="bg-papel text-left text-xs uppercase text-gris">
                <tr>
                  <th className="p-3">SKU</th><th className="p-3">Producto</th><th className="p-3">Stock</th><th className="p-3">Mín.</th>
                  <th className="p-3">B2C / B2B</th><th className="p-3">Recepción / ajuste</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((r) => {
                  const low = r.stock <= r.stock_min;
                  return (
                    <tr key={r.sku} className={`border-t ${low ? "bg-amber-50" : ""}`}>
                      <td className="p-3 font-mono text-xs">{r.sku}</td>
                      <td className="p-3">
                        <Link href={`/producto/${r.sku}`} className="font-medium hover:text-azul">{r.name}</Link>
                        <p className="text-xs text-gris">{r.category} · {r.supplier}{r.status !== "Activo" && " · Inactivo"}</p>
                      </td>
                      <td className={`p-3 font-bold ${r.stock === 0 ? "text-red-600" : low ? "text-amber-700" : ""}`}>
                        {r.stock}{low && " ⚠️"}
                      </td>
                      <td className="p-3 text-gris">{r.stock_min}</td>
                      <td className="p-3 whitespace-nowrap">{money(r.price_b2c)} / {money(r.price_b2b)}</td>
                      <td className="p-3">
                        <form action={adjustStock} className="flex gap-1">
                          <input type="hidden" name="sku" value={r.sku} />
                          <input type="hidden" name="back" value={back} />
                          <input name="qty" type="number" required placeholder="+/-" className="w-20 rounded-lg border border-linea px-2 py-1 text-xs" />
                          <select name="type" className="rounded-lg border border-linea px-1 text-xs">
                            <option value="ENTRADA_COMPRA">Recepción OC</option>
                            <option value="AJUSTE">Ajuste</option>
                          </select>
                          <button className="rounded-lg bg-tinta px-2 py-1 text-xs text-white">OK</button>
                        </form>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
          <nav className="mt-4 flex items-center justify-center gap-2 text-sm">
            {page > 1 && <Link href={qs(page - 1)} className="rounded-lg border bg-white px-3 py-1.5">← Anterior</Link>}
            <span className="text-gris">Página {page} de {pages} · {count.n} productos</span>
            {page < pages && <Link href={qs(page + 1)} className="rounded-lg border bg-white px-3 py-1.5">Siguiente →</Link>}
          </nav>
        </section>

        <aside className="space-y-4">
          <section className="rounded-lg border border-linea bg-white p-5">
            <h2 className="font-semibold">Carga masiva de catálogo</h2>
            <p className="mb-3 mt-1 text-xs text-gris">
              Sube el Excel “Catalogo_500_Productos.xlsx” o un CSV. Los SKUs nuevos se crean con su stock inicial; en los existentes se actualizan datos y precios, y se conserva el stock real.
            </p>
            <ImportForm />
          </section>
          <section className="rounded-lg border border-linea bg-white p-5">
            <h2 className="font-semibold">Stock por categoría</h2>
            <ul className="mt-2 space-y-1 text-sm">
              {byCat.map((c) => (
                <li key={c.category} className="flex justify-between"><span>{c.category}</span><span className="text-gris">{c.skus} SKUs · {c.units} u.</span></li>
              ))}
            </ul>
          </section>
          <section className="rounded-lg border border-linea bg-white p-5">
            <h2 className="font-semibold">Últimos movimientos</h2>
            {moves.length === 0 && <p className="mt-2 text-sm text-gris">Sin movimientos aún.</p>}
            <ul className="mt-2 space-y-1 text-xs">
              {moves.map((m, i) => (
                <li key={i} className="flex justify-between gap-2">
                  <span><span className="font-mono">{m.sku}</span> · {m.type.replaceAll("_", " ").toLowerCase()}</span>
                  <span className={m.qty < 0 ? "text-red-600" : "text-emerald-700"}>{m.qty > 0 ? "+" : ""}{m.qty}</span>
                </li>
              ))}
            </ul>
          </section>
        </aside>
      </div>
    </div>
  );
}
