import { NextResponse } from "next/server";
import { revalidatePath } from "next/cache";
import { isAdmin } from "@/lib/session";
import { tx } from "@/lib/db";
import { parseCsv, parseExcel } from "@/lib/catalog-parser";
import { importRows } from "@/lib/catalog-import";

// Carga masiva de productos al ERP desde Excel (.xlsx) o CSV.
export async function POST(req: Request) {
  if (!(await isAdmin())) return NextResponse.json({ error: "No autorizado" }, { status: 401 });
  const form = await req.formData();
  const file = form.get("file");
  if (!(file instanceof File) || file.size === 0) return NextResponse.json({ error: "Selecciona un archivo" }, { status: 400 });
  if (file.size > 4 * 1024 * 1024) return NextResponse.json({ error: "El archivo supera 4 MB" }, { status: 400 });
  const name = file.name.toLowerCase();
  let parsed;
  try {
    parsed = name.endsWith(".csv") ? parseCsv(await file.text()) : await parseExcel(await file.arrayBuffer());
  } catch {
    return NextResponse.json({ error: "No se pudo leer el archivo. Usa un .xlsx o .csv." }, { status: 400 });
  }
  if (!parsed.rows.length) return NextResponse.json({ error: "No se encontraron productos válidos", errors: parsed.errors.slice(0, 20) }, { status: 400 });
  const res = await tx((c) => importRows(c, parsed.rows));
  revalidatePath("/", "layout");
  return NextResponse.json({ ...res, total: parsed.rows.length, errors: parsed.errors.slice(0, 20) });
}
