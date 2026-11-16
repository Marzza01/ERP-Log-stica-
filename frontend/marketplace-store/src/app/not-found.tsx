import Link from "next/link";

export default function NotFound() {
  return (
    <div className="mx-auto max-w-md px-4 py-20 text-center">
      <p className="text-6xl">🔍</p>
      <h1 className="mt-4 ancho text-2xl font-bold">No encontramos lo que buscas</h1>
      <p className="mt-2 text-gris">El producto o pedido no existe o ya no está disponible.</p>
      <Link href="/catalogo" className="mt-6 inline-block rounded-md bg-azul px-6 py-3 font-semibold text-white">Ir al catálogo</Link>
    </div>
  );
}
