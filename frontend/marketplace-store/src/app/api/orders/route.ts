import { after, NextResponse } from "next/server";
import { erpConfig } from "@/lib/erp";
import { pushOrderToErp } from "@/lib/erp-sync";
import { createOrder, OrderError, type OrderInput } from "@/lib/orders";

// El ERP es la autoridad de catálogo, stock y confirmación cuando está configurado.
export async function POST(req: Request) {
  let body: OrderInput;
  try {
    body = await req.json();
  } catch {
    return NextResponse.json({ error: "Solicitud inválida" }, { status: 400 });
  }

  try {
    const erpRequired = Boolean(erpConfig());
    const result = await createOrder(body, erpRequired);

    if (erpRequired) {
      // El pedido permanece Pendiente ERP hasta que el OMS confirme su reserva.
      after(async () => {
        const sync = await pushOrderToErp(result.code);
        if ("ok" in sync && sync.ok === false) {
          console.error("LOGIMKT ERP order sync failed", { code: result.code, error: sync.error });
        }
      });
    }

    return NextResponse.json(result, { status: erpRequired ? 202 : 201 });
  } catch (e) {
    if (e instanceof OrderError) return NextResponse.json({ error: e.message }, { status: 422 });
    console.error("Error al crear pedido", e);
    return NextResponse.json({ error: "No se pudo crear el pedido. Intenta de nuevo." }, { status: 500 });
  }
}
