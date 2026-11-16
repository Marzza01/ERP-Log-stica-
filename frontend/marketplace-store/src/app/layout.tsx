import type { Metadata } from "next";
import Link from "next/link";
import { Suspense } from "react";
import { Archivo } from "next/font/google";
import { getMode } from "@/lib/session";
import { CartCount, CartProvider } from "@/components/cart";
import { ModeToggle } from "@/components/mode-toggle";
import "./globals.css";

const archivo = Archivo({ variable: "--font-archivo", subsets: ["latin"], axes: ["wdth"] });

export const metadata: Metadata = {
  title: "LOGIMKT Marketplace para personas y empresas",
  description: "Marketplace salvadoreño de electrónica y tecnología, ropa, alimentos, ferretería, hogar y salud, con precios B2B y B2C.",
};

const NAV = [
  ["Electrónica y Tecnología", "Electrónica y tecnología"],
  ["Ropa y Accesorios", "Ropa y accesorios"],
  ["Alimentos y Bebidas", "Alimentos y bebidas"],
  ["Ferretería e Industria", "Ferretería e industria"],
  ["Hogar y Decoración", "Hogar y decoración"],
  ["Salud y Cuidado Personal", "Salud y cuidado personal"],
];

export default async function RootLayout({ children }: LayoutProps<"/">) {
  const mode = await getMode();
  return (
    <html lang="es" className={`${archivo.variable} h-full antialiased`}>
      <body className="flex min-h-full flex-col bg-papel text-tinta">
        <CartProvider mode={mode}>
          <header className="sticky top-0 z-20 bg-azul text-white">
            <div className="mx-auto flex max-w-7xl flex-wrap items-center gap-x-6 gap-y-3 px-4 py-3">
              <Link href="/" className="display text-2xl tracking-tight" aria-label="LOGIMKT, inicio">
                LOGIMKT
              </Link>
              <form action="/catalogo" role="search" className="order-3 flex w-full flex-1 sm:order-none sm:w-auto">
                <label htmlFor="buscar" className="sr-only">Buscar productos</label>
                <input
                  id="buscar"
                  name="q"
                  placeholder="¿Qué necesitas hoy?"
                  className="w-full rounded-l-md border-0 bg-white px-4 py-2.5 text-sm text-tinta placeholder:text-gris focus:outline-none focus-visible:ring-2 focus-visible:ring-senal"
                />
                <button className="rounded-r-md bg-senal px-5 text-sm font-bold text-tinta hover:bg-senal-oscuro">Buscar</button>
              </form>
              <div className="ml-auto flex items-center gap-5">
                <Suspense>
                  <ModeToggle mode={mode} />
                </Suspense>
                <Link href="/pedido" className="hidden text-sm font-medium text-white/85 hover:text-white md:block">
                  Rastrear pedido
                </Link>
                <Link href="/carrito" className="relative flex items-center gap-2 text-sm font-semibold" aria-label="Ver carrito">
                  <svg viewBox="0 0 24 24" className="size-6" fill="none" stroke="currentColor" strokeWidth={1.8} aria-hidden>
                    <path d="M3 4h2l2.4 11.2a1.5 1.5 0 0 0 1.5 1.2h8.7a1.5 1.5 0 0 0 1.5-1.1L21 8H6.2" />
                    <circle cx="9.5" cy="20" r="1.3" />
                    <circle cx="17.5" cy="20" r="1.3" />
                  </svg>
                  <span className="hidden sm:inline">Carrito</span>
                  <CartCount />
                </Link>
              </div>
            </div>
            <nav className="border-t border-white/10 bg-azul-oscuro" aria-label="Categorías">
              <div className="mx-auto flex max-w-7xl gap-6 overflow-x-auto px-4 py-2 text-sm text-white/80">
                <Link href="/catalogo" className="whitespace-nowrap font-semibold text-white hover:underline">Todo el catálogo</Link>
                {NAV.map(([value, label]) => (
                  <Link key={value} href={`/catalogo?cat=${encodeURIComponent(value)}`} className="whitespace-nowrap hover:text-white hover:underline">
                    {label}
                  </Link>
                ))}
              </div>
            </nav>
            {mode === "B2B" && (
              <div className="bg-senal text-tinta">
                <p className="mx-auto max-w-7xl px-4 py-1.5 text-sm font-medium">
                  Estás viendo precios de empresa. Pedido mínimo de $500, con crédito a 30 días disponible.
                </p>
              </div>
            )}
          </header>
          <main className="flex-1">{children}</main>
          <footer className="mt-20 bg-tinta text-white/75">
            <div className="mx-auto grid max-w-7xl gap-8 px-4 py-10 text-sm sm:grid-cols-2 lg:grid-cols-[1.5fr_1fr_1fr_1fr]">
              <div>
                <p className="display text-xl text-white">LOGIMKT</p>
                <p className="mt-2 max-w-xs">Marketplace salvadoreño para hogares y negocios. San Salvador, El Salvador.</p>
              </div>
              <div className="flex flex-col gap-2">
                <p className="font-semibold text-white">Comprar</p>
                <Link href="/catalogo" className="hover:text-white">Catálogo completo</Link>
                <Link href="/pedido" className="hover:text-white">Rastrear un pedido</Link>
                <Link href="/carrito" className="hover:text-white">Mi carrito</Link>
              </div>
              <div className="flex flex-col gap-2">
                <p className="font-semibold text-white">Políticas</p>
                <Link href="/politicas/terminos" className="hover:text-white">Términos y condiciones</Link>
                <Link href="/politicas/envios" className="hover:text-white">Envíos y entregas</Link>
                <Link href="/politicas/devoluciones" className="hover:text-white">Devoluciones y reembolsos</Link>
                <Link href="/politicas/pagos" className="hover:text-white">Pagos y facturación</Link>
                <Link href="/politicas/privacidad" className="hover:text-white">Privacidad</Link>
                <Link href="/politicas" className="hover:text-white">Ver todas</Link>
              </div>
              <div className="flex flex-col gap-2">
                <p className="font-semibold text-white">Operación</p>
                <Link href="/admin" className="hover:text-white">Panel ERP</Link>
                <p className="text-xs text-white/50">LOGIMKT · El Salvador. Pagos reales y pasarelas deshabilitados; validación manual en el ERP.</p>
              </div>
            </div>
          </footer>
        </CartProvider>
      </body>
    </html>
  );
}
