import Link from "next/link";
import { getMode } from "@/lib/session";
import { getCategories, searchProducts } from "@/lib/products";
import { ProductCard } from "@/components/product-card";

type SP = Record<string, string | string[] | undefined>;
const one = (v: string | string[] | undefined) => (Array.isArray(v) ? v[0] : v) ?? "";
const numOrUndef = (v: string) => (v !== "" && Number.isFinite(Number(v)) ? Number(v) : undefined);

export default async function Catalogo({ searchParams }: { searchParams: Promise<SP> }) {
  const sp = await searchParams;
  const mode = await getMode();
  const f = {
    q: one(sp.q).trim(),
    cat: one(sp.cat),
    sub: one(sp.sub),
    min: one(sp.min),
    max: one(sp.max),
    disp: one(sp.disp) === "1",
    orden: one(sp.orden),
    page: Math.max(1, Number(one(sp.page)) || 1),
  };
  const [cats, result] = await Promise.all([
    getCategories(),
    searchProducts(
      {
        q: f.q || undefined, cat: f.cat || undefined, sub: f.sub || undefined,
        min: numOrUndef(f.min), max: numOrUndef(f.max), available: f.disp, sort: f.orden, page: f.page,
      },
      mode === "B2B" ? "price_b2b" : "price_b2c",
    ),
  ]);
  const subs = cats.find((c) => c.category === f.cat)?.subs ?? [];

  const link = (patch: Record<string, string>) => {
    const p = new URLSearchParams();
    const merged = { q: f.q, cat: f.cat, sub: f.sub, min: f.min, max: f.max, disp: f.disp ? "1" : "", orden: f.orden, page: "", ...patch };
    for (const [k, v] of Object.entries(merged)) if (v) p.set(k, v);
    return `/catalogo?${p.toString()}`;
  };

  return (
    <div className="mx-auto max-w-7xl px-4 py-6">
      <h1 className="ancho text-2xl font-bold">
        {f.q ? `Resultados para “${f.q}”` : f.cat || "Todo el catálogo"}
      </h1>
      <p className="text-sm text-gris">
        {result.total} productos, con {mode === "B2B" ? "precios de empresa" : "precios para personas"}
      </p>

      <div className="mt-6 grid gap-6 lg:grid-cols-[260px_1fr]">
        {/* Filtros */}
        <aside>
          <form action="/catalogo" className="space-y-5 rounded-lg border border-linea bg-white p-4 text-sm">
            <div>
              <label className="font-semibold" htmlFor="q">Buscar</label>
              <input id="q" name="q" defaultValue={f.q} placeholder="Nombre o SKU" className="mt-1 w-full rounded-lg border border-linea px-3 py-2" />
            </div>
            <div>
              <label className="font-semibold" htmlFor="cat">Categoría</label>
              <select id="cat" name="cat" defaultValue={f.cat} className="mt-1 w-full rounded-lg border border-linea px-3 py-2">
                <option value="">Todas</option>
                {cats.map((c) => (
                  <option key={c.category} value={c.category}>{c.category} ({c.n})</option>
                ))}
              </select>
            </div>
            {subs.length > 0 && (
              <div>
                <label className="font-semibold" htmlFor="sub">Subcategoría</label>
                <select id="sub" name="sub" defaultValue={f.sub} className="mt-1 w-full rounded-lg border border-linea px-3 py-2">
                  <option value="">Todas</option>
                  {subs.map((s) => <option key={s} value={s}>{s}</option>)}
                </select>
              </div>
            )}
            <div>
              <span className="font-semibold">Precio</span>
              <div className="mt-1 flex gap-2">
                <input name="min" type="number" min={0} step="0.01" defaultValue={f.min} placeholder="Mín $" className="w-full rounded-lg border border-linea px-3 py-2" />
                <input name="max" type="number" min={0} step="0.01" defaultValue={f.max} placeholder="Máx $" className="w-full rounded-lg border border-linea px-3 py-2" />
              </div>
            </div>
            <label className="flex items-center gap-2">
              <input type="checkbox" name="disp" value="1" defaultChecked={f.disp} className="size-4" />
              Solo productos disponibles
            </label>
            <div>
              <label className="font-semibold" htmlFor="orden">Ordenar por</label>
              <select id="orden" name="orden" defaultValue={f.orden} className="mt-1 w-full rounded-lg border border-linea px-3 py-2">
                <option value="">Relevancia</option>
                <option value="precio_asc">Precio: menor a mayor</option>
                <option value="precio_desc">Precio: mayor a menor</option>
                <option value="nombre">Nombre A-Z</option>
                <option value="valoracion">Mejor valorados</option>
              </select>
            </div>
            <div className="flex gap-2">
              <button className="flex-1 rounded-lg bg-azul py-2 font-semibold text-white hover:bg-azul-oscuro">Aplicar</button>
              <Link href="/catalogo" className="rounded-lg border border-linea px-3 py-2 text-gris hover:bg-papel">Limpiar</Link>
            </div>
          </form>
        </aside>

        <section>
          {result.rows.length === 0 ? (
            <div className="rounded-lg border border-dashed border-linea bg-white p-10 text-center text-gris">
              No encontramos productos con esos filtros.
            </div>
          ) : (
            <div className="grid grid-cols-2 gap-4 md:grid-cols-3 xl:grid-cols-4">
              {result.rows.map((p) => <ProductCard key={p.sku} p={p} mode={mode} />)}
            </div>
          )}
          {result.pages > 1 && (
            <nav className="mt-8 flex items-center justify-center gap-2 text-sm">
              {result.page > 1 && (
                <Link href={link({ page: String(result.page - 1) })} className="rounded-lg border border-linea bg-white px-3 py-2 hover:bg-papel">← Anterior</Link>
              )}
              <span className="px-3 text-gris">Página {result.page} de {result.pages}</span>
              {result.page < result.pages && (
                <Link href={link({ page: String(result.page + 1) })} className="rounded-lg border border-linea bg-white px-3 py-2 hover:bg-papel">Siguiente →</Link>
              )}
            </nav>
          )}
        </section>
      </div>
    </div>
  );
}
