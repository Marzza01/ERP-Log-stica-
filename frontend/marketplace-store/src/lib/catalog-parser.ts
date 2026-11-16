import ExcelJS from "exceljs";

export type CatalogRow = {
  sku: string;
  name: string;
  category: string;
  subcategory: string | null;
  price_b2c: number;
  price_b2b: number;
  stock: number;
  stock_min: number;
  supplier: string | null;
  origin_country: string | null;
  weight_kg: number | null;
  length_cm: number | null;
  width_cm: number | null;
  height_cm: number | null;
  unit: string | null;
  status: string;
  ean: string | null;
  description: string | null;
  image_url: string | null;
};

// Normaliza encabezados: "Precio B2C ($)" -> "precioB2C" en minúsculas sin acentos ni símbolos.
function norm(s: unknown): string {
  return String(s ?? "")
    .normalize("NFD")
    .replace(/[̀-ͯ]/g, "")
    .toLowerCase()
    .replace(/[^a-z0-9]/g, "");
}

const HEADER_MAP: Record<string, keyof CatalogRow> = {
  sku: "sku",
  codigo: "sku",
  nombredelproducto: "name",
  nombre: "name",
  producto: "name",
  categoria: "category",
  subcategoria: "subcategory",
  preciob2c: "price_b2c",
  preciob2b: "price_b2b",
  stockinicial: "stock",
  stock: "stock",
  stockminimo: "stock_min",
  proveedor: "supplier",
  paisorigen: "origin_country",
  pesokg: "weight_kg",
  peso: "weight_kg",
  largocm: "length_cm",
  anchocm: "width_cm",
  altocm: "height_cm",
  unidaddemedida: "unit",
  unidad: "unit",
  estado: "status",
  codigoean: "ean",
  ean: "ean",
  descripcioncorta: "description",
  descripcion: "description",
  imagen: "image_url",
  imagenurl: "image_url",
  urlimagen: "image_url",
  foto: "image_url",
};

function cellText(v: ExcelJS.CellValue): string {
  if (v === null || v === undefined) return "";
  if (typeof v === "object") {
    if (v instanceof Date) return v.toISOString();
    if ("result" in v) return cellText(v.result as ExcelJS.CellValue);
    if ("richText" in v) return v.richText.map((t) => t.text).join("");
    if ("text" in v) return String(v.text);
    return "";
  }
  return String(v).trim();
}

function num(v: string): number | null {
  if (!v) return null;
  const n = Number(v.replace(/[^0-9.\-]/g, ""));
  return Number.isFinite(n) ? n : null;
}

export type ParseResult = { rows: CatalogRow[]; errors: string[] };

export function parseRows(header: string[], data: string[][]): ParseResult {
  const cols = header.map((h) => HEADER_MAP[norm(h)]);
  const rows: CatalogRow[] = [];
  const errors: string[] = [];
  const seen = new Set<string>();
  data.forEach((cells, i) => {
    const rec: Record<string, string> = {};
    cols.forEach((c, j) => {
      if (c && rec[c] === undefined) rec[c] = (cells[j] ?? "").trim();
    });
    if (!rec.sku) return; // fila vacía
    const line = `Fila ${i + 1} (${rec.sku})`;
    const b2c = num(rec.price_b2c);
    const b2b = num(rec.price_b2b);
    if (!rec.name) errors.push(`${line}: falta el nombre`);
    if (!rec.category) errors.push(`${line}: falta la categoría`);
    if (b2c === null || b2c < 0) errors.push(`${line}: precio B2C inválido`);
    if (b2b === null || b2b < 0) errors.push(`${line}: precio B2B inválido`);
    // Precio en cero: se importa al ERP pero queda Inactivo (no se publica en la tienda hasta corregirlo).
    const zeroPrice = b2c === 0 || b2b === 0;
    if (zeroPrice) errors.push(`${line}: precio en cero, se importó como Inactivo`);
    if (seen.has(rec.sku)) {
      errors.push(`${line}: SKU duplicado, se omitió`);
      return;
    }
    seen.add(rec.sku);
    if (!rec.name || !rec.category || b2c === null || b2b === null || b2c < 0 || b2b < 0) return;
    rows.push({
      sku: rec.sku,
      name: rec.name,
      category: rec.category,
      subcategory: rec.subcategory || null,
      price_b2c: b2c,
      price_b2b: b2b,
      stock: Math.max(0, Math.round(num(rec.stock) ?? 0)),
      stock_min: Math.max(0, Math.round(num(rec.stock_min) ?? 0)),
      supplier: rec.supplier || null,
      origin_country: rec.origin_country || null,
      weight_kg: num(rec.weight_kg),
      length_cm: num(rec.length_cm),
      width_cm: num(rec.width_cm),
      height_cm: num(rec.height_cm),
      unit: rec.unit || null,
      status: zeroPrice ? "Inactivo" : rec.status || "Activo",
      ean: rec.ean || null,
      description: rec.description || null,
      image_url: /^(https?:\/\/|\/)/.test(rec.image_url ?? "") ? rec.image_url : null,
    });
  });
  return { rows, errors };
}

/** Lee un Excel (.xlsx) del catálogo. Busca la fila de encabezados que contiene "SKU". */
export async function parseExcel(buffer: ArrayBuffer): Promise<ParseResult> {
  const wb = new ExcelJS.Workbook();
  await wb.xlsx.load(buffer);
  const ws =
    wb.worksheets.find((w) => norm(w.name).includes("catalogo")) ?? wb.worksheets[0];
  if (!ws) return { rows: [], errors: ["El archivo no tiene hojas"] };
  let headerRow = -1;
  for (let r = 1; r <= Math.min(20, ws.rowCount); r++) {
    const vals = (ws.getRow(r).values as ExcelJS.CellValue[]).map(cellText);
    if (vals.some((v) => norm(v) === "sku")) {
      headerRow = r;
      break;
    }
  }
  if (headerRow < 0) return { rows: [], errors: ["No se encontró la columna SKU"] };
  const width = ws.columnCount;
  const readRow = (r: number) => {
    const row = ws.getRow(r);
    const out: string[] = [];
    for (let c = 1; c <= width; c++) out.push(cellText(row.getCell(c).value));
    return out;
  };
  const header = readRow(headerRow);
  const data: string[][] = [];
  for (let r = headerRow + 1; r <= ws.rowCount; r++) data.push(readRow(r));
  return parseRows(header, data);
}

/** Lee un CSV simple (separado por comas o punto y coma, con comillas). */
export function parseCsv(text: string): ParseResult {
  const lines = text.replace(/^﻿/, "").split(/\r?\n/).filter((l) => l.trim());
  if (!lines.length) return { rows: [], errors: ["El archivo está vacío"] };
  const sep = (lines[0].match(/;/g)?.length ?? 0) > (lines[0].match(/,/g)?.length ?? 0) ? ";" : ",";
  const split = (line: string) => {
    const out: string[] = [];
    let cur = "";
    let q = false;
    for (let i = 0; i < line.length; i++) {
      const ch = line[i];
      if (q) {
        if (ch === '"' && line[i + 1] === '"') { cur += '"'; i++; }
        else if (ch === '"') q = false;
        else cur += ch;
      } else if (ch === '"') q = true;
      else if (ch === sep) { out.push(cur); cur = ""; }
      else cur += ch;
    }
    out.push(cur);
    return out;
  };
  const all = lines.map(split);
  const h = all.findIndex((r) => r.some((v) => norm(v) === "sku"));
  if (h < 0) return { rows: [], errors: ["No se encontró la columna SKU"] };
  return parseRows(all[h], all.slice(h + 1));
}
