import type { Metadata } from "next";
import Link from "next/link";
import { POLICIES, POLICIES_UPDATED } from "@/lib/policies";

export const metadata: Metadata = {
  title: "Políticas · LOGIMKT",
  description: "Términos, pagos, envíos, cancelaciones, devoluciones y privacidad del marketplace LOGIMKT.",
};

export default function PoliticasPage() {
  return (
    <div className="mx-auto max-w-5xl px-4 py-10">
      <p className="text-sm font-semibold text-azul">Centro de ayuda</p>
      <h1 className="mt-1 display text-4xl">Políticas de LOGIMKT</h1>
      <p className="mt-3 max-w-2xl text-gris">
        Las reglas con las que funciona el marketplace para personas y empresas. Coinciden con lo que hace el sistema:
        los mínimos, plazos, estados del pedido y métodos de pago que ves aquí son los mismos que aplica la tienda y el ERP.
      </p>
      <ul className="mt-8 grid gap-4 sm:grid-cols-2">
        {POLICIES.map((p) => (
          <li key={p.slug}>
            <Link
              href={`/politicas/${p.slug}`}
              className="flex h-full gap-4 rounded-lg border border-linea bg-white p-5 transition hover:border-azul hover:shadow-sm"
            >
              <span className="text-3xl" aria-hidden>{p.icon}</span>
              <span>
                <span className="block font-bold text-tinta">{p.title}</span>
                <span className="mt-1 block text-sm text-gris">{p.summary}</span>
              </span>
            </Link>
          </li>
        ))}
      </ul>
      <p className="mt-8 text-xs text-gris">Última actualización: {POLICIES_UPDATED}.</p>
    </div>
  );
}
