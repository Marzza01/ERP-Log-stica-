import Link from "next/link";
import type { Product } from "@/lib/products";
import { categoryStyle, money, type Channel } from "@/lib/shared";
import { AddToCartButton } from "./cart";
import { productImage } from "@/lib/product-image";

const kg = (w: number | null) => (w === null ? null : w < 1 ? `${Math.round(w * 1000)} g` : `${w.toLocaleString("es-SV", { maximumFractionDigits: 1 })} kg`);

/** Imagen del producto sobre el color de su categoría, con una etiqueta de bulto que muestra peso y medidas reales. */
export function ProductVisual({
  p,
  size = "md",
}: {
  p: Pick<Product, "category" | "subcategory" | "name" | "image_url" | "weight_kg" | "length_cm" | "width_cm" | "height_cm">;
  size?: "md" | "lg";
}) {
  const s = categoryStyle(p.category);
  const dims = p.length_cm ? `${p.length_cm}×${p.width_cm}×${p.height_cm} cm` : null;
  const weight = kg(p.weight_kg);
  const src = productImage(p);
  const photo = !src.startsWith("/productos/");
  return (
    <div
      className={`visual relative flex items-center justify-center overflow-hidden rounded-md ${size === "lg" ? "aspect-square" : "aspect-[5/4]"}`}
      style={{ "--tono": s.bg } as React.CSSProperties}
    >
      {/* eslint-disable-next-line @next/next/no-img-element -- imágenes locales pequeñas o URLs externas del ERP */}
      <img
        src={src}
        alt={p.name}
        loading="lazy"
        decoding="async"
        width={256}
        height={256}
        className={
          photo
            ? "absolute inset-0 size-full object-cover"
            : `relative drop-shadow-[0_14px_14px_rgba(21,34,59,0.18)] transition-transform duration-300 group-hover:-translate-y-1 group-hover:scale-105 ${size === "lg" ? "w-[44%]" : "w-[52%]"}`
        }
      />
      {(weight || dims) && (
        <span
          className={`absolute bottom-2 left-2 rounded-sm bg-kraft-claro font-medium text-kraft-oscuro shadow-[0_1px_0_rgba(0,0,0,0.08)] ${size === "lg" ? "px-3 py-1.5 text-sm" : "px-2 py-0.5 text-[11px]"}`}
        >
          {[weight, dims].filter(Boolean).join(" / ")}
        </span>
      )}
    </div>
  );
}

export function Stars({ rating, count }: { rating: number | null; count?: number }) {
  if (!rating) return <span className="text-xs text-gris">Sin reseñas todavía</span>;
  const full = Math.round(rating);
  return (
    <span className="inline-flex items-center gap-1 text-xs" title={`${rating.toFixed(1)} de 5 estrellas`}>
      <span className="text-senal-oscuro" aria-hidden>
        {"★".repeat(full)}
        <span className="text-linea">{"★".repeat(5 - full)}</span>
      </span>
      <span className="sr-only">{rating.toFixed(1)} de 5 estrellas</span>
      {count !== undefined && <span className="text-gris">({count})</span>}
    </span>
  );
}

export function StockBadge({ stock, min }: { stock: number; min: number }) {
  if (stock <= 0) return <span className="text-xs font-semibold text-alerta">Sin existencias</span>;
  if (stock <= min) return <span className="text-xs font-semibold text-alerta">Quedan {stock}</span>;
  return <span className="text-xs text-ok">{stock} disponibles</span>;
}

export function ProductCard({ p, mode }: { p: Product; mode: Channel }) {
  const unavailableB2B = mode === "B2B" && p.price_b2b <= 0;
  const price = mode === "B2B" ? p.price_b2b : p.price_b2c;
  const saving = p.price_b2c > 0 && p.price_b2b > 0 ? Math.round((1 - p.price_b2b / p.price_b2c) * 100) : 0;
  return (
    <article className="group flex flex-col rounded-lg border border-linea bg-white p-3 transition-colors hover:border-azul">
      <Link href={`/producto/${encodeURIComponent(p.sku)}`} className="flex flex-1 flex-col focus-visible:outline-offset-4">
        <ProductVisual p={p} />
        <p className="mt-3 text-xs text-gris">{p.subcategory ?? p.category}</p>
        <h3 className="mt-0.5 line-clamp-2 min-h-[2.5rem] text-[15px] font-semibold leading-tight group-hover:text-azul">{p.name}</h3>
        <div className="mt-1.5"><Stars rating={p.rating} count={p.reviews || undefined} /></div>
        <div className="mt-3 flex items-baseline gap-2">
          <span className="ancho text-xl font-bold">{unavailableB2B ? "No disponible B2B" : money(price)}</span>
          {mode === "B2B" && saving > 0 && (
            <span className="text-xs text-gris">
              <span className="line-through">{money(p.price_b2c)}</span> <span className="font-semibold text-ok">{saving}% menos</span>
            </span>
          )}
        </div>
        <div className="mb-3 mt-1">
          <StockBadge stock={p.stock} min={p.stock_min} />
        </div>
      </Link>
      <AddToCartButton
        product={{ sku: p.sku, name: p.name, category: p.category, image: productImage(p), price_b2c: p.price_b2c, price_b2b: p.price_b2b, stock: p.stock }}
        disabled={unavailableB2B}
        disabledLabel="No disponible B2B"
      />
    </article>
  );
}
