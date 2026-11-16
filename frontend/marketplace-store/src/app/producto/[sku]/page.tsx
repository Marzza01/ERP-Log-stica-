import Link from "next/link";
import { notFound } from "next/navigation";
import { getMode } from "@/lib/session";
import { getProduct, getRelated, getReviews } from "@/lib/products";
import { money } from "@/lib/shared";
import { AddToCartButton } from "@/components/cart";
import { productImage } from "@/lib/product-image";
import { ProductCard, ProductVisual, Stars, StockBadge } from "@/components/product-card";
import { addReview } from "@/app/actions";
import { Barcode } from "@/components/barcode";

export default async function ProductPage({ params, searchParams }: PageProps<"/producto/[sku]">) {
  const { sku } = await params;
  const sp = await searchParams;
  const p = await getProduct(decodeURIComponent(sku));
  if (!p || p.status !== "Activo") notFound();
  const mode = await getMode();
  const [reviews, related] = await Promise.all([getReviews(p.sku), getRelated(p)]);
  const unavailableB2B = mode === "B2B" && p.price_b2b <= 0;
  const price = mode === "B2B" ? p.price_b2b : p.price_b2c;
  const brand = p.description?.split("|")[1]?.trim();

  return (
    <div className="mx-auto max-w-7xl px-4 py-6">
      <nav className="text-sm text-gris">
        <Link href="/catalogo" className="hover:text-azul">Catálogo</Link> ›{" "}
        <Link href={`/catalogo?cat=${encodeURIComponent(p.category)}`} className="hover:text-azul">{p.category}</Link> ›{" "}
        <Link href={`/catalogo?cat=${encodeURIComponent(p.category)}&sub=${encodeURIComponent(p.subcategory ?? "")}`} className="hover:text-azul">{p.subcategory}</Link>
      </nav>

      <div className="mt-4 grid gap-8 md:grid-cols-2">
        <ProductVisual p={p} size="lg" />
        <div>
          {brand && <p className="text-sm font-semibold text-azul">{brand}</p>}
          <h1 className="mt-1 display text-3xl">{p.name}</h1>
          <div className="mt-2"><Stars rating={p.rating} count={p.reviews} /></div>
          <div className="mt-5 rounded-lg border border-linea bg-white p-5">
            <div className="flex items-baseline gap-3">
              <span className="ancho text-4xl font-extrabold">{unavailableB2B ? "No disponible B2B" : money(price)}</span>
              <span className="text-sm text-gris">por {p.unit?.toLowerCase() ?? "unidad"}</span>
            </div>
            {mode === "B2B" ? (
              <p className="mt-1 text-sm text-emerald-700">
                Precio de empresa. Las personas pagan {money(p.price_b2c)}.
              </p>
            ) : (
              <p className="mt-1 text-sm text-gris">
                Si compras para tu empresa: <strong>{p.price_b2b > 0 ? money(p.price_b2b) : "no disponible para B2B"}</strong> (pedido mínimo de $500).
              </p>
            )}
            <div className="mt-3"><StockBadge stock={p.stock} min={p.stock_min} /></div>
            <div className="mt-4">
              <AddToCartButton
                withQty
                product={{ sku: p.sku, name: p.name, category: p.category, image: productImage(p), price_b2c: p.price_b2c, price_b2b: p.price_b2b, stock: p.stock }}
                disabled={unavailableB2B}
                disabledLabel="No disponible B2B"
              />
            </div>
            <p className="mt-3 text-xs text-gris">🚚 Entrega estimada: {mode === "B2B" ? "5" : "3"} días hábiles</p>
          </div>

          <div className="mt-5 overflow-hidden rounded-lg border border-linea bg-white">
            <dl className="grid grid-cols-2 gap-x-4 gap-y-3 p-5 text-sm">
              {[
                ["SKU", p.sku],
                ["Proveedor", p.supplier],
                ["País de origen", p.origin_country],
                ["Unidad", p.unit],
                ["Peso", p.weight_kg ? `${p.weight_kg} kg` : null],
                ["Medidas del bulto", p.length_cm ? `${p.length_cm} × ${p.width_cm} × ${p.height_cm} cm` : null],
              ].map(([k, v]) => v && (
                <div key={k}>
                  <dt className="text-gris">{k}</dt>
                  <dd className="font-medium">{v}</dd>
                </div>
              ))}
            </dl>
            {p.ean && (
              <div className="flex items-center justify-between gap-4 border-t-2 border-dashed border-linea px-5 py-3">
                <span className="text-xs text-gris">Código EAN-13</span>
                <Barcode code={p.ean} className="h-12 w-40 text-tinta" />
              </div>
            )}
          </div>
        </div>
      </div>

      {/* Reseñas */}
      <section id="resenas" className="mt-12 grid gap-8 md:grid-cols-[1fr_360px]">
        <div>
          <h2 className="ancho text-2xl font-bold">Opiniones de clientes</h2>
          {reviews.length === 0 ? (
            <p className="mt-3 text-gris">Aún no hay reseñas. ¡Sé el primero en opinar!</p>
          ) : (
            <ul className="mt-4 space-y-3">
              {reviews.map((r) => (
                <li key={r.id} className="rounded-lg border border-linea bg-white p-4">
                  <div className="flex items-center justify-between">
                    <span className="font-semibold">{r.author}</span>
                    <Stars rating={r.rating} />
                  </div>
                  {r.comment && <p className="mt-1 text-sm text-tinta">{r.comment}</p>}
                  <p className="mt-1 text-xs text-gris">{new Date(r.created_at).toLocaleDateString("es-SV")}</p>
                </li>
              ))}
            </ul>
          )}
        </div>
        <form action={addReview} className="h-fit space-y-3 rounded-lg border border-linea bg-white p-5 text-sm">
          <h3 className="text-lg font-semibold">Califica este producto</h3>
          {sp.resena === "ok" && <p className="rounded-lg bg-emerald-50 p-2 text-emerald-700">¡Gracias por tu reseña!</p>}
          {sp.resena === "error" && <p className="rounded-lg bg-red-50 p-2 text-red-700">Completa tu nombre y la calificación.</p>}
          <input type="hidden" name="sku" value={p.sku} />
          <input name="author" required maxLength={60} placeholder="Tu nombre" className="w-full rounded-lg border border-linea px-3 py-2" />
          <select name="rating" required defaultValue="5" className="w-full rounded-lg border border-linea px-3 py-2">
            <option value="5">★★★★★ Excelente</option>
            <option value="4">★★★★ Muy bueno</option>
            <option value="3">★★★ Regular</option>
            <option value="2">★★ Malo</option>
            <option value="1">★ Muy malo</option>
          </select>
          <textarea name="comment" rows={3} maxLength={1000} placeholder="Cuéntanos tu experiencia (opcional)" className="w-full rounded-lg border border-linea px-3 py-2" />
          <button className="w-full rounded-lg bg-azul py-2 font-semibold text-white hover:bg-azul-oscuro">Publicar reseña</button>
        </form>
      </section>

      {related.length > 0 && (
        <section className="mt-12">
          <h2 className="ancho text-2xl font-bold">También te puede interesar</h2>
          <div className="mt-4 grid grid-cols-2 gap-4 md:grid-cols-4">
            {related.map((r) => <ProductCard key={r.sku} p={r} mode={mode} />)}
          </div>
        </section>
      )}
    </div>
  );
}
