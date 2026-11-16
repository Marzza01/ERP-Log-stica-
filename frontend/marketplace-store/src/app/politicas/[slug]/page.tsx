import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { POLICIES, POLICIES_UPDATED, getPolicy, type PolicySection } from "@/lib/policies";

export function generateStaticParams() {
  return POLICIES.map((p) => ({ slug: p.slug }));
}

export const dynamicParams = false;

export async function generateMetadata({ params }: PageProps<"/politicas/[slug]">): Promise<Metadata> {
  const p = getPolicy((await params).slug);
  return { title: p ? `${p.title} · LOGIMKT` : "Políticas · LOGIMKT", description: p?.summary };
}

function Section({ s }: { s: PolicySection }) {
  return (
    <section className="mt-8">
      <h2 className="ancho text-xl font-bold">{s.title}</h2>
      {s.paragraphs?.map((t, i) => <p key={i} className="mt-3 leading-relaxed">{t}</p>)}
      {s.items && (
        <ul className="mt-3 list-disc space-y-2 pl-5 leading-relaxed marker:text-azul">
          {s.items.map((t, i) => <li key={i}>{t}</li>)}
        </ul>
      )}
      {s.table && (
        <div className="mt-4 overflow-x-auto rounded-lg border border-linea bg-white">
          <table className="w-full text-left text-sm">
            <thead className="bg-azul-claro text-azul-oscuro">
              <tr>{s.table[0].map((h, i) => <th key={i} scope="col" className="px-4 py-2 font-semibold">{h}</th>)}</tr>
            </thead>
            <tbody>
              {s.table.slice(1).map((row, r) => (
                <tr key={r} className="border-t border-linea align-top">
                  {row.map((c, i) =>
                    i === 0 ? <th key={i} scope="row" className="px-4 py-2 font-semibold">{c}</th> : <td key={i} className="px-4 py-2">{c}</td>,
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}

export default async function PolicyPage({ params }: PageProps<"/politicas/[slug]">) {
  const policy = getPolicy((await params).slug);
  if (!policy) notFound();

  return (
    <div className="mx-auto grid max-w-6xl gap-10 px-4 py-8 lg:grid-cols-[220px_1fr]">
      <nav aria-label="Políticas" className="text-sm lg:sticky lg:top-24 lg:self-start">
        <Link href="/politicas" className="font-semibold text-azul hover:underline">← Todas las políticas</Link>
        <ul className="mt-4 hidden space-y-1 lg:block">
          {POLICIES.map((p) => (
            <li key={p.slug}>
              <Link
                href={`/politicas/${p.slug}`}
                aria-current={p.slug === policy.slug ? "page" : undefined}
                className={`block rounded-md px-3 py-1.5 ${p.slug === policy.slug ? "bg-azul text-white" : "text-gris hover:bg-white hover:text-tinta"}`}
              >
                {p.title}
              </Link>
            </li>
          ))}
        </ul>
      </nav>
      <article className="max-w-3xl">
        <p className="text-4xl" aria-hidden>{policy.icon}</p>
        <h1 className="mt-2 display text-4xl">{policy.title}</h1>
        <p className="mt-2 text-gris">{policy.summary}</p>
        <p className="mt-1 text-xs text-gris">Vigente desde el {POLICIES_UPDATED}.</p>
        {policy.sections.map((s) => <Section key={s.title} s={s} />)}
        <p className="mt-10 rounded-lg bg-kraft-claro p-4 text-sm text-kraft-oscuro">
          ¿Dudas sobre un pedido? Ten a mano tu código LMK y consulta su estado en{" "}
          <Link href="/pedido" className="font-semibold underline">Rastrear un pedido</Link>.
        </p>
      </article>
    </div>
  );
}
