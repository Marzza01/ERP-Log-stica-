"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { useCart } from "@/components/cart";
import { B2B_MIN_ORDER, B2C_FREE_SHIPPING_FROM, B2C_SHIPPING, PAYMENT_METHODS, money } from "@/lib/shared";

const CITIES = ["San Salvador", "Santa Tecla", "Soyapango", "Santa Ana", "San Miguel", "Mejicanos", "Apopa", "Antiguo Cuscatlán", "La Libertad", "Sonsonate", "Usulután", "Ahuachapán", "Otra"];

export default function CheckoutPage() {
  const { items, ready, subtotal, priceOf, mode, clear } = useCart();
  const router = useRouter();
  const methods = PAYMENT_METHODS[mode];
  const [payment, setPayment] = useState(methods[0].id);
  const [error, setError] = useState<string | null>(null);
  const [sending, setSending] = useState(false);

  if (!ready) return <div className="mx-auto max-w-5xl px-4 py-10 text-gris">Cargando…</div>;
  if (!items.length)
    return (
      <div className="mx-auto max-w-5xl px-4 py-16 text-center">
        <h1 className="ancho text-2xl font-bold">No hay productos para pagar</h1>
        <Link href="/catalogo" className="mt-6 inline-block rounded-md bg-azul px-6 py-3 font-semibold text-white">Ir al catálogo</Link>
      </div>
    );

  const shipping = mode === "B2C" && subtotal < B2C_FREE_SHIPPING_FROM ? B2C_SHIPPING : 0;
  const tax = Math.round(subtotal * 0.13 * 100) / 100;
  const total = Math.round((subtotal + tax + shipping) * 100) / 100;
  const selected = methods.find((m) => m.id === payment) ? payment : methods[0].id;
  const unavailableItems = mode === "B2B" ? items.filter((item) => item.price_b2b <= 0) : [];
  const hasUnavailableItems = unavailableItems.length > 0;

  async function onSubmit(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    setError(null);
    const fd = new FormData(e.currentTarget);
    setSending(true);
    try {
      const res = await fetch("/api/orders", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          channel: mode,
          customer_name: fd.get("name"),
          customer_email: fd.get("email"),
          customer_phone: fd.get("phone"),
          company: fd.get("company"),
          tax_id: fd.get("tax_id"),
          address: fd.get("address"),
          city: fd.get("city"),
          payment_method: selected,
          items: items.map((i) => ({ sku: i.sku, qty: i.qty })),
        }),
      });
      const data = await res.json();
      if (!res.ok) throw new Error(data.error || "No se pudo crear el pedido");
      clear();
      router.push(`/pedido/${data.code}?nuevo=1${data.partial ? "&parcial=1" : ""}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Error inesperado");
      setSending(false);
    }
  }

  const input = "w-full rounded-lg border border-linea px-3 py-2";
  return (
    <div className="mx-auto max-w-6xl px-4 py-6">
      <h1 className="ancho text-2xl font-bold">Finalizar compra</h1>
      <p className="text-sm text-gris">{mode === "B2B" ? "Compra de empresa" : "Compra personal"}</p>
      <form onSubmit={onSubmit} className="mt-6 grid gap-6 lg:grid-cols-[1fr_360px]">
        <div className="space-y-6">
          <fieldset className="space-y-3 rounded-lg border border-linea bg-white p-5">
            <legend className="px-1 text-lg font-semibold">1. Tus datos</legend>
            {mode === "B2B" && (
              <div className="grid gap-3 sm:grid-cols-2">
                <input name="company" required placeholder="Nombre de la empresa" className={input} />
                <input name="tax_id" required placeholder="NIT o NRC" className={input} />
              </div>
            )}
            <div className="grid gap-3 sm:grid-cols-2">
              <input name="name" required placeholder={mode === "B2B" ? "Persona de contacto" : "Nombre completo"} className={input} />
              <input name="email" type="email" required placeholder="Correo electrónico" className={input} />
              <input name="phone" placeholder="Teléfono (opcional)" className={input} />
            </div>
          </fieldset>

          <fieldset className="space-y-3 rounded-lg border border-linea bg-white p-5">
            <legend className="px-1 text-lg font-semibold">2. Dirección de entrega</legend>
            <input name="address" required placeholder="Dirección completa" className={input} />
            <select name="city" required defaultValue="San Salvador" className={input}>
              {CITIES.map((c) => <option key={c}>{c}</option>)}
            </select>
          </fieldset>

          <fieldset className="space-y-3 rounded-lg border border-linea bg-white p-5">
            <legend className="px-1 text-lg font-semibold">3. Método de pago</legend>
            {methods.map((m) => (
              <label key={m.id} className={`flex cursor-pointer items-center gap-3 rounded-md border p-3 ${selected === m.id ? "border-azul bg-azul-claro" : "border-linea"}`}>
                <input type="radio" name="payment" value={m.id} checked={selected === m.id} onChange={() => setPayment(m.id)} />
                <span>
                  <span className="block font-medium">{m.label}</span>
                  <span className="text-xs text-gris">{m.hint}</span>
                </span>
              </label>
            ))}
            {selected === "transferencia" && (
              <p className="rounded-md bg-papel p-3 text-sm text-gris">
                La transferencia debe realizarse únicamente con los datos bancarios oficiales que LOGIMKT confirme al validar el pedido.
                No envíes dinero a cuentas recibidas por canales no verificados.
              </p>
            )}
          </fieldset>
        </div>

        <aside className="h-fit rounded-lg border border-linea bg-white p-5">
          <h2 className="text-lg font-semibold">Tu pedido</h2>
          <ul className="mt-3 max-h-72 space-y-2 overflow-auto text-sm">
            {items.map((i) => (
              <li key={i.sku} className="flex justify-between gap-2">
                <span className="text-gris">{i.qty} × {i.name}</span>
                <span className="whitespace-nowrap">{mode === "B2B" && i.price_b2b <= 0 ? "No disponible B2B" : money(priceOf(i) * i.qty)}</span>
              </li>
            ))}
          </ul>
          <dl className="mt-4 space-y-1 border-t pt-3 text-sm">
            <div className="flex justify-between"><dt>Subtotal</dt><dd>{money(subtotal)}</dd></div>
            <div className="flex justify-between"><dt>IVA (13%)</dt><dd>{money(tax)}</dd></div>
            <div className="flex justify-between"><dt>Envío</dt><dd>{shipping ? money(shipping) : "Gratis"}</dd></div>
            <div className="flex justify-between pt-1 text-base font-bold"><dt>Total</dt><dd>{money(total)}</dd></div>
          </dl>
          {hasUnavailableItems && (
            <p className="mt-3 rounded-lg bg-kraft-claro p-3 text-sm text-kraft-oscuro">
              No tienen precio mayorista habilitado: {unavailableItems.map((item) => item.sku).join(", ")}. Quítalos del carrito para continuar.
            </p>
          )}
          {mode === "B2B" && subtotal < B2B_MIN_ORDER && (
            <p className="mt-3 rounded-lg bg-kraft-claro p-3 text-sm text-kraft-oscuro">El pedido mínimo para empresas es de {money(B2B_MIN_ORDER)}.</p>
          )}
          {error && <p className="mt-3 rounded-lg bg-red-50 p-3 text-sm text-red-700">{error}</p>}
          <button
            disabled={sending || (mode === "B2B" && subtotal < B2B_MIN_ORDER) || hasUnavailableItems}
            className="mt-4 w-full rounded-md bg-senal py-3 font-semibold text-tinta hover:bg-senal-oscuro disabled:bg-linea"
          >
            {sending ? "Enviando…" : `Enviar pedido de ${money(total)}`}
          </button>
          <p className="mt-2 text-center text-xs text-gris">El pedido queda pendiente hasta que el ERP valide y reserve el inventario. No se realiza ningún cobro real.</p>
          <p className="mt-1 text-center text-xs text-gris">
            Al confirmar aceptas los <Link href="/politicas/terminos" className="underline hover:text-azul">términos</Link> y las políticas de{" "}
            <Link href="/politicas/envios" className="underline hover:text-azul">envío</Link> y{" "}
            <Link href="/politicas/devoluciones" className="underline hover:text-azul">devoluciones</Link>.
          </p>
        </aside>
      </form>
    </div>
  );
}
