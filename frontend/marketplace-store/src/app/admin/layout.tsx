import Link from "next/link";
import { isAdmin } from "@/lib/session";
import { adminLogin, adminLogout } from "@/app/actions";
import { LoginErrorClient } from "./login-error";

export default async function AdminLayout({ children }: LayoutProps<"/admin">) {
  if (!(await isAdmin())) {
    return (
      <div className="mx-auto max-w-sm px-4 py-16">
        <h1 className="ancho text-2xl font-bold">Panel ERP · LOGIMKT</h1>
        <p className="mt-1 text-sm text-gris">Inventario, pedidos y KPIs logísticos. Solo personal autorizado.</p>
        <LoginErrorClient />
        <form action={adminLogin} className="mt-6 space-y-3">
          <input type="password" name="password" required placeholder="Contraseña de administrador" className="w-full rounded-lg border border-linea px-3 py-2" />
          <button className="w-full rounded-lg bg-azul py-2 font-semibold text-white hover:bg-azul-oscuro">Entrar</button>
        </form>
      </div>
    );
  }
  return (
    <div className="mx-auto max-w-7xl px-4 py-6">
      <div className="flex flex-wrap items-center gap-2 border-b border-linea pb-3">
        <span className="mr-4 text-lg font-bold">Panel ERP</span>
        {[
          ["/admin", "Dashboard KPIs"],
          ["/admin/pedidos", "Pedidos (OMS)"],
          ["/admin/inventario", "Inventario"],
          ["/admin/erp", "Conexión ERP"],
        ].map(([href, label]) => (
          <Link key={href} href={href} className="rounded-lg px-3 py-1.5 text-sm font-medium text-tinta hover:bg-linea">{label}</Link>
        ))}
        <form action={adminLogout} className="ml-auto">
          <button className="text-sm text-gris hover:text-red-600">Cerrar sesión</button>
        </form>
      </div>
      <div className="pt-6">{children}</div>
    </div>
  );
}
