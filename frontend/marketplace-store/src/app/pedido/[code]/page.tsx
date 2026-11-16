import Link from "next/link";
import { notFound } from "next/navigation";
import { getOrder } from "@/lib/orders";
import { ORDER_FLOW, PAYMENT_METHODS, money, statusColor } from "@/lib/shared";
import { submitNps } from "@/app/actions";
import { Barcode } from "@/components/barcode";
import { refreshErpOrder } from "@/lib/erp-sync";

const guideDigits = (t: string) => t.replace(/\D/g, "").padEnd(12, "0").slice(0, 12);

const dt = (d: Date | null) =>
  d ? new Date(d).toLocaleString("es-SV", { dateStyle: "medium", timeStyle: "short", timeZone: "America/El_Salvador" }) : "—";

export default async function PedidoPage({ params, searchParams }: PageProps<"/pedido/[code]">) {
  const { code } = await params;
  const sp = await searchParams;
  const data = await getOrder(decodeURIComponent(code).toUpperCase());
  if (!data) notFound();
  const { order: o, items, events } = data;
  const erp = await refreshErpOrder(o.code);
  const stepIdx = ORDER_FLOW.indexOf(o.status as (typeof ORDER_FLOW)[number]);
  const method = [...PAYMENT_METHODS.B2C, ...PAYMENT_METHODS.B2B].find((m) => m.id === o.payment_method)?.label ?? o.payment_method;
  const canRate = ["Entregado", "Cerrado", "Devuelto"].includes(o.status);

  return (
    <div className="mx-auto max-w-5xl px-4 py-6">
      {sp.nuevo && (
        <div className="mb-6 rounded-lg border-2 border-ok bg-white p-5">
          {o.status === "Pendiente ERP" ? (
            <>
              <p className="text-lg font-bold">Recibimos tu pedido.</p>
              <p className="text-sm">
                Tu pedido <strong>{o.code}</strong> está pendiente de confirmación de inventario por el ERP.
                Todavía no está confirmado ni se ha realizado ningún cobro. Conserva este código para consultar su estado.
              </p>
            </>
          ) : (
            <>
              <p className="text-lg font-bold">¡Gracias! Tu pedido fue recibido.</p>
              <p className="text-sm">
                Pedido <strong>{o.code}</strong> confirmado{o.erp_order_id ? " en el ERP" : " en modo local"}.
                {o.erp_order_id ? " El inventario quedó reservado en el ERP." : " La tienda está funcionando sin conexión al ERP."}
                {" "}Guarda este número para rastrearlo.
              </p>
            </>
          )}
          {sp.parcial && (
            <p className="mt-2 text-sm text-amber-800">Algunos productos no tenían stock suficiente y se surtieron parcialmente.</p>
          )}
        </div>
      )}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="ancho text-2xl font-bold">Pedido {o.code}</h1>
          <p className="text-sm text-gris">Hecho el {dt(o.created_at)}, {o.channel === "B2B" ? "compra de empresa" : "compra personal"}</p>
        </div>
        <span className={`rounded-full px-4 py-1.5 text-sm font-semibold ${statusColor(o.status)}`}>{o.status}</span>
      </div>

      {/* Línea de tiempo */}
      {stepIdx >= 0 && (
        <ol className="mt-6 grid grid-cols-3 gap-2 sm:grid-cols-6">
          {ORDER_FLOW.map((s, i) => (
            <li key={s} className="text-center">
              <div className={`mx-auto flex size-9 items-center justify-center rounded-full text-sm font-bold ${i <= stepIdx ? "bg-azul text-white" : "bg-linea text-gris"}`}>
                {i < stepIdx ? "✓" : i + 1}
              </div>
              <p className={`mt-1 text-xs ${i <= stepIdx ? "font-semibold text-tinta" : "text-gris"}`}>{s}</p>
            </li>
          ))}
        </ol>
      )}

      <div className="mt-8 grid gap-6 md:grid-cols-[1fr_320px]">
        <div className="space-y-6">
          <section className="rounded-lg border border-linea bg-white p-5">
            <h2 className="font-semibold">Seguimiento</h2>
            {o.tracking && (
              <div className="mt-3 flex items-center justify-between gap-4 rounded-md border-2 border-tinta bg-white p-4 text-sm">
                <div>
                  <p className="text-xs text-gris">Transportista</p>
                  <p className="font-bold">{o.carrier}</p>
                  <p className="mt-2 text-xs text-gris">Número de guía</p>
                  <p className="ancho font-bold">{o.tracking}</p>
                </div>
                <Barcode code={guideDigits(o.tracking)} className="h-14 w-40 text-tinta" showText={false} />
              </div>
            )}
            <ul className="mt-3 space-y-3 border-l-2 border-linea pl-4">
              {[...events].reverse().map((e, i) => (
                <li key={i} className="relative">
                  <span className="absolute -left-[1.4rem] top-1 size-3 rounded-md bg-azul" />
                  <p className="text-sm font-semibold">{e.status}</p>
                  <p className="text-xs text-gris">{dt(e.created_at)}</p>
                  {e.note && <p className="text-sm text-gris">{e.note}</p>}
                </li>
              ))}
            </ul>
          </section>
          <section className="rounded-lg border border-linea bg-white p-5">
            <h2 className="font-semibold">Productos</h2>
            <table className="mt-3 w-full text-sm">
              <tbody>
                {items.map((it) => (
                  <tr key={it.sku} className="border-b last:border-0">
                    <td className="py-2">
                      <Link href={`/producto/${it.sku}`} className="hover:text-azul">{it.name}</Link>
                      {it.qty < it.qty_requested && (
                        <span className="block text-xs text-amber-700">Solicitado {it.qty_requested}, surtido {it.qty}</span>
                      )}
                    </td>
                    <td className="py-2 text-right text-gris">{it.qty} × {money(it.unit_price)}</td>
                    <td className="py-2 text-right font-medium">{money(it.line_total)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </section>
        </div>

        <aside className="space-y-4">
          <section className="rounded-lg bg-white p-5 text-sm shadow-sm">
            <h2 className="font-semibold">Resumen</h2>
            <dl className="mt-2 space-y-1">
              <div className="flex justify-between"><dt>Subtotal</dt><dd>{money(o.subtotal)}</dd></div>
              <div className="flex justify-between"><dt>IVA (13%)</dt><dd>{money(o.tax_amount)}</dd></div>
              <div className="flex justify-between"><dt>Envío</dt><dd>{o.shipping_charge > 0 ? money(o.shipping_charge) : "Gratis"}</dd></div>
              <div className="flex justify-between font-bold"><dt>Total</dt><dd>{money(o.total)}</dd></div>
            </dl>
            <p className="mt-3 text-gris">Pago: {method}</p>
            <p className="text-gris">Estado del pago: {o.payment_status}</p>
            <p className="mt-3 text-gris">Entregar a {o.company ? `${o.company}, ` : ""}{o.customer_name}</p>
            <p className="text-gris">{o.address}, {o.city}</p>
            <p className="mt-3 text-gris">Entrega prometida: <strong>{dt(o.promised_at)}</strong></p>
            {o.delivered_at && <p className="text-gris">Entregado: <strong>{dt(o.delivered_at)}</strong></p>}
            {(erp || o.erp_order_number) && (
              <p className="mt-3 rounded-md bg-azul-claro p-3 text-tinta">
                Registrado en el ERP como <strong>{erp?.numeroOrden ?? o.erp_order_number}</strong>
                {(erp?.estado ?? o.erp_status) && <>, estado <strong>{erp?.estado ?? o.erp_status}</strong></>}
                {erp?.guiaEnvio && <>, guía {erp.guiaEnvio}</>}.
              </p>
            )}
          </section>

          {canRate && (
            <section className="rounded-lg bg-white p-5 text-sm shadow-sm">
              <h2 className="font-semibold">Encuesta de satisfacción</h2>
              {o.nps_score !== null || sp.encuesta ? (
                <p className="mt-2 text-emerald-700">¡Gracias! Tu respuesta nos ayuda a mejorar.</p>
              ) : (
                <form action={submitNps} className="mt-2 space-y-3">
                  <input type="hidden" name="code" value={o.code} />
                  <p className="text-gris">Del 0 al 10, ¿qué tan probable es que recomiendes LOGIMKT a un amigo?</p>
                  <div className="grid grid-cols-11 gap-1">
                    {Array.from({ length: 11 }, (_, n) => (
                      <label key={n} className="cursor-pointer">
                        <input type="radio" name="score" value={n} required className="peer sr-only" />
                        <span className="block rounded-md border border-linea py-1 text-center text-xs peer-checked:border-blue-700 peer-checked:bg-azul peer-checked:text-white">{n}</span>
                      </label>
                    ))}
                  </div>
                  <textarea name="comment" rows={2} placeholder="Comentario (opcional)" className="w-full rounded-lg border border-linea px-3 py-2" />
                  <button className="w-full rounded-lg bg-azul py-2 font-semibold text-white hover:bg-azul-oscuro">Enviar</button>
                </form>
              )}
            </section>
          )}
        </aside>
      </div>
    </div>
  );
}
