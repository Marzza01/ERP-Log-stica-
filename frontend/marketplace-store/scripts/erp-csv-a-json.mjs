import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const source = path.join(root, "data", "Catalogo_500_Productos_ERP.csv");
const target = path.join(root, "data", "catalogo.json");

function parseCsv(text) {
  const rows = [];
  let row = [];
  let cell = "";
  let quoted = false;
  const input = text.replace(/^\uFEFF/, "");
  for (let i = 0; i < input.length; i++) {
    const ch = input[i];
    if (quoted) {
      if (ch === '"' && input[i + 1] === '"') {
        cell += '"';
        i++;
      } else if (ch === '"') {
        quoted = false;
      } else {
        cell += ch;
      }
    } else if (ch === '"') {
      quoted = true;
    } else if (ch === ",") {
      row.push(cell);
      cell = "";
    } else if (ch === "\n") {
      row.push(cell.replace(/\r$/, ""));
      if (row.some((value) => value.trim() !== "")) rows.push(row);
      row = [];
      cell = "";
    } else {
      cell += ch;
    }
  }
  if (cell.length || row.length) {
    row.push(cell.replace(/\r$/, ""));
    if (row.some((value) => value.trim() !== "")) rows.push(row);
  }
  return rows;
}

function dimensionValues(value) {
  const match = String(value ?? "").match(/(\d+(?:[.,]\d+)?)\s*[x×]\s*(\d+(?:[.,]\d+)?)\s*[x×]\s*(\d+(?:[.,]\d+)?)/i);
  if (!match) return ["", "", ""];
  return match.slice(1).map((part) => part.replace(",", "."));
}

const records = parseCsv(fs.readFileSync(source, "utf8"));
if (records.length < 2) throw new Error("El catálogo ERP está vacío.");
const headers = records[0].map((value) => value.trim().replace(/^\uFEFF/, ""));
const index = (name) => {
  const i = headers.findIndex((header) => header.normalize("NFD").replace(/[\u0300-\u036f]/g, "").toLowerCase() === name.normalize("NFD").replace(/[\u0300-\u036f]/g, "").toLowerCase());
  if (i < 0) throw new Error(`Falta la columna obligatoria: ${name}`);
  return i;
};
const col = {
  sku: index("SKU"),
  name: index("Nombre del Producto"),
  category: index("Categoría"),
  subcategory: index("Subcategoría"),
  b2c: index("Precio B2C ($)"),
  b2b: index("Precio B2B ($)"),
  stock: index("Stock Inicial"),
  min: index("Stock Mínimo"),
  supplier: index("Proveedor"),
  origin: index("País Origen"),
  weight: index("Peso (kg)"),
  dimensions: index("Dimensiones (cm)"),
  unit: index("Unidad de Medida"),
  status: index("Estado"),
  ean: index("Código EAN"),
  description: index("Descripción Corta"),
};

const outputHeader = [
  "SKU", "Nombre del Producto", "Categoría", "Subcategoría",
  "Precio B2C ($)", "Precio B2B ($)", "Stock Inicial", "Stock Mínimo",
  "Proveedor", "País Origen", "Peso (kg)", "Largo (cm)", "Ancho (cm)",
  "Alto (cm)", "Unidad de Medida", "Estado", "Código EAN", "Descripción Corta",
];
const rows = records.slice(1).map((record, i) => {
  const dimensions = dimensionValues(record[col.dimensions]);
  const row = [
    record[col.sku], record[col.name], record[col.category], record[col.subcategory],
    record[col.b2c], record[col.b2b], record[col.stock], record[col.min],
    record[col.supplier], record[col.origin], record[col.weight],
    ...dimensions, record[col.unit], record[col.status], record[col.ean],
    record[col.description],
  ];
  if (!row[0] || !row[1] || !row[2]) throw new Error(`Fila ERP incompleta: ${i + 2}`);
  return row;
});

const skus = rows.map((row) => row[0]);
const eans = rows.map((row) => row[16]).filter(Boolean);
if (rows.length !== 500) throw new Error(`Se esperaban 500 productos ERP; se encontraron ${rows.length}.`);
if (new Set(skus).size !== rows.length) throw new Error("El catálogo ERP contiene SKU duplicados.");
if (new Set(eans).size !== eans.length) throw new Error("El catálogo ERP contiene EAN duplicados.");

fs.writeFileSync(target, JSON.stringify({ header: outputHeader, rows }, null, 2) + "\n", "utf8");
console.log(`Catálogo tienda regenerado desde ERP: ${rows.length} productos, ${new Set(skus).size} SKU y ${new Set(eans).size} EAN únicos.`);
