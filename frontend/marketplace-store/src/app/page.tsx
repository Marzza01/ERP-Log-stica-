import Link from "next/link";
import { getMode } from "@/lib/session";
import { getBestSellers, getCategories, getFeatured } from "@/lib/products";
import { B2B_MIN_ORDER, B2C_FREE_SHIPPING_FROM, categoryStyle, money } from "@/lib/shared";
import { categoryImage, productImage } from "@/lib/product-image";
import { ProductCard } from "@/components/product-card";
import { Barcode } from "@/components/barcode";

function addBusinessDays(d: Date, n: number) {
  const out = new Date(d);
  while (n > 0) {
    out.setDate(out.getDate() + 1);
    if (out.getDay() !== 0 && out.getDay() !== 6) n--;
  }
  return out;
}

const label = (c: string) => c;

export default async function Home() {
  const mode = await getMode();
  const [cats, featured, best] = await Promise.all([getCategories(), getFeatured(8), getBestSellers(4)]);
  const total = cats.reduce((s, c) => s + c.n, 0);
  const now = new Date();
  const eta = addBusinessDays(now, mode === "B2B" ? 5 : 3).toLocaleDateString("es-SV", {
    weekday: "long", day: "numeric", month: "long", timeZone: "America/El_Salvador",
  });
  const guide = `74${now.getFullYear() % 100}${String(now.getMonth() + 1).padStart(2, "0")}${String(now.getDate()).padStart(2, "0")}0315`;
  const compare = featured.filter((p) => p.price_b2c > p.price_b2b).slice(0, 4);

  return (
    <div>
      {/* Portada: la promesa de entrega contada como una guía de envío */}
      <section className="bg-azul text-white">
        <div className="mx-auto grid max-w-7xl items-center gap-12 px-4 pb-16 pt-12 lg:grid-cols-[1.1fr_0.9fr]">
          <div>
            <h1 className="display max-w-xl text-5xl sm:text-6xl">
              {mode === "B2B" ? "Del clic a tu negocio en 5 días." : "Del clic a tu puerta en 3 días."}
            </h1>
            <p className="mt-6 max-w-lg text-lg text-white/85">
              {total} productos de vendedores salvadoreños, con el stock al día y seguimiento de tu pedido en cada paso.
              Compras desde $5 y el envío es gratis desde {money(B2C_FREE_SHIPPING_FROM)}.
            </p>
            <div className="mt-8 flex flex-wrap gap-3">
              <Link href="/catalogo" className="rounded-md bg-senal px-6 py-3 font-bold text-tinta hover:bg-senal-oscuro">
                Ver el catálogo
              </Link>
              <Link href="/pedido" className="rounded-md border border-white/40 px-6 py-3 font-semibold hover:bg-white/10">
                Rastrear un pedido
              </Link>
            </div>
          </div>

          <div className="relative mx-auto w-full max-w-md px-2 sm:px-0">
            <div className="etiqueta-entrada rotate-[-1.5deg] rounded-sm bg-white text-tinta shadow-[0_18px_40px_-12px_rgba(0,0,0,0.45)]">
              <div className="cinta absolute -top-3 left-1/2 h-7 w-32 -translate-x-1/2 rotate-[2deg] opacity-90" aria-hidden />
              <div className="flex items-center justify-between border-b-2 border-tinta px-5 py-3">
                <span className="display text-lg">LOGIMKT</span>
                <span className="text-sm font-semibold">Guía de envío</span>
              </div>
              <dl className="grid grid-cols-2 border-b border-linea text-sm">
                <div className="border-r border-linea px-5 py-3">
                  <dt className="text-xs text-gris">Sale de</dt>
                  <dd className="font-semibold">Bodega central, San Salvador</dd>
                </div>
                <div className="px-5 py-3">
                  <dt className="text-xs text-gris">Llega a</dt>
                  <dd className="font-semibold">{mode === "B2B" ? "Tu negocio" : "Tu casa"}, en todo el país</dd>
                </div>
              </dl>
              <ol className="space-y-2.5 px-5 py-4 text-sm" aria-label="Recorrido de un pedido">
                {[
                  ["Haces clic en comprar", "done"],
                  ["Reservamos tu stock en el acto", "done"],
                  ["Preparamos y empacamos", "now"],
                  ["Sale con transportista y número de guía", "next"],
                  [`Llega el ${eta}`, "next"],
                ].map(([t, st]) => (
                  <li key={t} className="flex items-center gap-3">
                    <span
                      className={`flex size-5 shrink-0 items-center justify-center rounded-full border-2 text-[10px] font-bold ${
                        st === "done" ? "border-azul bg-azul text-white" : st === "now" ? "border-azul bg-senal" : "border-linea"
                      }`}
                      aria-hidden
                    >
                      {st === "done" ? "✓" : ""}
                    </span>
                    <span className={st === "next" ? "text-gris" : "font-medium"}>{t}</span>
                  </li>
                ))}
              </ol>
              <div className="flex items-end justify-between gap-4 border-t-2 border-dashed border-linea px-5 py-4">
                <Barcode code={guide} className="h-14 w-44 text-tinta" />
                <p className="text-right text-xs text-gris">
                  Entrega prometida
                  <span className="ancho block text-2xl font-extrabold text-tinta">{mode === "B2B" ? "5" : "3"} días</span>
                </p>
              </div>
            </div>
          </div>
        </div>
      </section>

      <div className="mx-auto max-w-7xl px-4">
        {/* Categorías */}
        <section className="mt-12" aria-labelledby="cat-title">
          <h2 id="cat-title" className="ancho text-2xl font-bold">Compra por categoría</h2>
          <div className="mt-5 grid grid-cols-2 gap-2 sm:grid-cols-4 lg:grid-cols-7">
            {cats.map((c) => {
              const s = categoryStyle(c.category);
              return (
                <Link
                  key={c.category}
                  href={`/catalogo?cat=${encodeURIComponent(c.category)}`}
                  className="flex items-center gap-3 rounded-md px-3 py-3 transition-[filter] hover:brightness-95"
                  style={{ background: s.bg, color: s.fg }}
                >
                  {/* eslint-disable-next-line @next/next/no-img-element */}
                  <img src={categoryImage(c.category)} alt="" width={44} height={44} className="size-11 shrink-0 drop-shadow-sm" />
                  <span>
                    <span className="block text-sm font-bold leading-tight">{label(c.category)}</span>
                    <span className="text-xs opacity-80">{c.n} productos</span>
                  </span>
                </Link>
              );
            })}
          </div>
        </section>

        {best.length > 0 && (
          <section className="mt-14" aria-labelledby="best-title">
            <h2 id="best-title" className="ancho text-2xl font-bold">Lo que más se está vendiendo</h2>
            <div className="mt-5 grid grid-cols-2 gap-4 md:grid-cols-4">
              {best.map((p) => <ProductCard key={p.sku} p={p} mode={mode} />)}
            </div>
          </section>
        )}

        <section className="mt-14" aria-labelledby="feat-title">
          <div className="flex items-end justify-between gap-4">
            <h2 id="feat-title" className="ancho text-2xl font-bold">Con existencias para entrega inmediata</h2>
            <Link href="/catalogo?disp=1" className="shrink-0 text-sm font-semibold text-azul hover:underline">Ver todos</Link>
          </div>
          <div className="mt-5 grid grid-cols-2 gap-4 md:grid-cols-4">
            {featured.map((p) => <ProductCard key={p.sku} p={p} mode={mode} />)}
          </div>
        </section>

        {/* Empresas: comparación real de precios del catálogo */}
        <section className="mt-16 grid overflow-hidden rounded-lg bg-tinta text-white lg:grid-cols-[1fr_1.2fr]" aria-labelledby="b2b-title">
          <div className="p-8 sm:p-10">
            <h2 id="b2b-title" className="display text-3xl">Si compras para tu negocio, pagas precio de mayoreo.</h2>
            <p className="mt-4 max-w-md text-white/80">
              Cambia a “Mi empresa” arriba y verás los precios B2B en todo el catálogo. Pedido mínimo de {money(B2B_MIN_ORDER)},
              crédito a 30 días o transferencia, sujetos a autorización administrativa.
            </p>
            {mode === "B2C" ? (
              <p className="mt-6 inline-block rounded-md bg-senal px-4 py-2 text-sm font-bold text-tinta">Usa el botón “Mi empresa” del encabezado</p>
            ) : (
              <p className="mt-6 text-sm font-semibold text-senal">Ya estás viendo precios de empresa.</p>
            )}
          </div>
          <div className="bg-white/5 p-8 sm:p-10">
            <table className="w-full text-sm">
              <caption className="mb-3 text-left text-white/70">Ejemplos del catálogo de hoy</caption>
              <thead>
                <tr className="border-b border-white/20 text-left text-white/60">
                  <th className="py-2 font-medium">Producto</th>
                  <th className="py-2 text-right font-medium">Personas</th>
                  <th className="py-2 text-right font-medium">Empresas</th>
                </tr>
              </thead>
              <tbody>
                {compare.map((p) => (
                  <tr key={p.sku} className="border-b border-white/10">
                    <td className="py-3 pr-3">
                      <Link href={`/producto/${p.sku}`} className="flex items-center gap-3 hover:underline">
                        {/* eslint-disable-next-line @next/next/no-img-element */}
                        <img src={productImage(p)} alt="" width={32} height={32} className="size-8 shrink-0 object-contain" />
                        {p.name}
                      </Link>
                    </td>
                    <td className="py-3 text-right text-white/60">{money(p.price_b2c)}</td>
                    <td className="ancho py-3 text-right font-bold text-senal">{money(p.price_b2b)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>

        <section className="mt-14 grid gap-6 border-t border-linea pt-10 text-sm sm:grid-cols-3" aria-label="Cómo compras con nosotros">
          <div>
            <p className="font-bold">Pagas como prefieras</p>
            <p className="mt-1 text-gris">Transferencia bancaria o pago contra entrega. Las empresas pueden solicitar crédito a 30 días, sujeto a autorización.</p>
          </div>
          <div>
            <p className="font-bold">Sabes dónde va tu pedido</p>
            <p className="mt-1 text-gris">Recibes un número de pedido y una guía de envío para seguirlo desde la bodega hasta tu puerta.</p>
          </div>
          <div>
            <p className="font-bold">Si algo falla, lo devuelves</p>
            <p className="mt-1 text-gris">Registramos la solicitud en el ERP, inspeccionamos el producto y Finanzas autoriza el ajuste que corresponda.</p>
          </div>
        </section>
      </div>
    </div>
  );
}
