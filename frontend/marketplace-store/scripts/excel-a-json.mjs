// Convierte data/Catalogo_500_Productos.xlsx en data/catalogo.json (carga inicial automática del ERP).
// Uso: node scripts/excel-a-json.mjs [ruta.xlsx]
import ExcelJS from "exceljs";
import { writeFileSync } from "node:fs";
const file = process.argv[2] ?? "data/Catalogo_500_Productos.xlsx";
const wb = new ExcelJS.Workbook();
await wb.xlsx.readFile(file);
const ws = wb.getWorksheet("Catalogo 500 Productos") ?? wb.worksheets[0];
const t = (v) => (v && typeof v === "object" ? ("result" in v ? v.result : v.text ?? "") : v ?? "");
const header = ws.getRow(3).values.slice(1).map(String);
const rows = [];
for (let r = 4; r <= ws.rowCount; r++) {
  const vals = [];
  for (let c = 1; c <= header.length; c++) vals.push(String(t(ws.getRow(r).getCell(c).value)).trim());
  if (vals[0]) rows.push(vals);
}
writeFileSync("data/catalogo.json", JSON.stringify({ header, rows }));
console.log(`OK: ${rows.length} filas -> data/catalogo.json`);
