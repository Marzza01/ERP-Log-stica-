"use client";

import Link from "next/link";
import { useCart } from "@/components/cart";
import { B2B_MIN_ORDER, B2C_FREE_SHIPPING_FROM, B2C_SHIPPING, categoryStyle, money } from "@/lib/shared";
import { productImage } from "@/lib/product-image";

export default function CarritoPage() {
  const { items, ready, subtotal, priceOf, setQty, remove, mode } = useCart();
  if (!ready) return <div className="mx-auto max-w-5xl px-4 py-10 text-gris">Cargando carrito…</div>;

  if (!items.length)
    return (
      <div className="mx-auto max-w-5xl px-4 py-16 text-center">
        <p className="text-6xl">🛒</p>
        <h1 className="mt-4 ancho text-2xl font-bold">Tu carrito está vacío</h1>
        <Link href="/catalogo" className="mt-6 inline-block rounded-md bg-azul px-6 py-3 font-semibold text-white">Ir al catálogo</Link>
      </div>
    );

  const shipping = mode === "B2C" && subtotal < B2C_FREE_SHIPPING_FROM ? B2C_SHIPPING : 0;
  const missingB2B = mode === "B2B" ? Math.max(0, B2B_MIN_ORDER - subtotal) : 0;

  return (
    <div className="mx-auto max-w-6xl px-4 py-6">
      <h1 className="ancho text-2xl font-bold">Carrito de compras</h1>
      <p className="text-sm text-gris">{mode === "B2B" ? "Con precios de empresa" : "Con precios para personas"}</p>
      <div className="mt-6 grid gap-6 lg:grid-cols-[1fr_340px]">
        <ul className="space-y-3">
          {items.map((i) => {
            const s = categoryStyle(i.category);
            return (
              <li key={i.sku} className="flex items-center gap-4 rounded-lg border border-linea bg-white p-4">
                <div className="visual flex size-16 shrink-0 items-center justify-center overflow-hidden rounded-md" style={{ "--tono": s.bg } as React.CSSProperties}>
                  {/* eslint-disable-next-line @next/next/no-img-element */}
                  <img src={i.image ?? productImage(i)} alt="" className={i.image && !i.image.startsWith("/productos/") ? "size-full object-cover" : "size-11 drop-shadow"} />
                </div>
                <div className="min-w-0 flex-1">
                  <Link href={`/producto/${encodeURIComponent(i.sku)}`} className="font-semibold hover:text-azul">{i.name}</Link>
                  <p className="text-xs text-gris">{money(priceOf(i))} por unidad</p>
                  <button onClick={() => remove(i.sku)} className="mt-1 text-xs text-alerta hover:underline">Quitar</button>
                </div>
                <input
                  type="number"
                  min={1}
                  max={i.stock}
                  value={i.qty}
                  onChange={(e) => setQty(i.sku, Number(e.target.value) || 1)}
                  className="w-20 rounded-lg border border-linea px-2 py-1.5 text-sm"
                  aria-label={`Cantidad de ${i.name}`}
                />
                <p className="w-24 text-right font-semibold">{money(priceOf(i) * i.qty)}</p>
              </li>
            );
          })}
        </ul>
        <aside className="h-fit rounded-lg border border-linea bg-white p-5">
          <h2 className="text-lg font-semibold">Resumen</h2>
          <dl className="mt-3 space-y-2 text-sm">
            <div className="flex justify-between"><dt>Subtotal</dt><dd>{money(subtotal)}</dd></div>
            <div className="flex justify-between"><dt>Envío</dt><dd>{shipping ? money(shipping) : "Gratis"}</dd></div>
            <div className="flex justify-between border-t pt-2 text-base font-bold"><dt>Total</dt><dd>{money(subtotal + shipping)}</dd></div>
          </dl>
          {mode === "B2C" && shipping > 0 && (
            <p className="mt-3 text-xs text-gris">Agrega {money(B2C_FREE_SHIPPING_FROM - subtotal)} más para envío gratis.</p>
          )}
          {missingB2B > 0 ? (
            <p className="mt-4 rounded-lg bg-kraft-claro p-3 text-sm text-kraft-oscuro">
              El pedido mínimo para empresas es de {money(B2B_MIN_ORDER)}. Te faltan <strong>{money(missingB2B)}</strong>.
            </p>
          ) : (
            <Link href="/checkout" className="mt-4 block rounded-md bg-senal py-3 text-center font-semibold text-tinta hover:bg-senal-oscuro">
              Continuar al pago
            </Link>
          )}
          <Link href="/catalogo" className="mt-3 block text-center text-sm text-azul hover:underline">Seguir comprando</Link>
        </aside>
      </div>
    </div>
  );
}
