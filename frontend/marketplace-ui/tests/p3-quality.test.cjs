const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const ts = require('typescript');

function loadTs(source, dependency = {}, globals = {}) {
  const full = path.join(__dirname, '..', source);
  const transpiled = ts.transpileModule(fs.readFileSync(full, 'utf8'), {
    compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS },
    reportDiagnostics: true,
  });
  assert.equal(transpiled.diagnostics?.length ?? 0, 0);
  const module = { exports: {} };
  vm.runInNewContext(transpiled.outputText, { module, exports: module.exports, require: () => dependency, URLSearchParams, Number, String, ...globals }, { filename: source });
  return module.exports;
}

const catalog = loadTs('src/api/productos.ts', { apiClient: {} });
const report = loadTs('src/utils/kpiReport.ts');

test('P3 catálogo: sumar stock multiubicación sin contar reservas/bloqueos como disponible', () => {
  const p = catalog.normalizarProducto({
    id: 'demo', sku: 'SKU-1', stockMinimo: 4, fechaCreacion: '2026-11-08',
    stocks: [
      { cantidadTotal: 10, cantidadReservada: 2, cantidadBloqueada: 1 },
      { cantidadTotal: 6, cantidadReservada: 1, cantidadBloqueada: 2 },
    ],
  });
  assert.equal(p.stock.cantidadTotal, 16);
  assert.equal(p.stock.cantidadReservada, 3);
  assert.equal(p.stock.cantidadDisponible, 10);
  assert.equal(p.stock.esBajoMinimo, false);
  assert.equal(p.stocks, undefined);
});

test('P3 catálogo: filtros y paginación preservan total del servidor, nunca primera página como universo', () => {
  const response = catalog.normalizarPaginaProductos({ pagina: 2, tamano: 50, total: 500, items: [
    { id: 'demo', sku: 'A', stockMinimo: 1, stocks: [{ cantidadTotal: 2, cantidadBloqueada: 1, cantidadReservada: 0 }] },
  ] });
  assert.equal(response.total, 500);
  assert.equal(response.pagina, 2);
  assert.equal(response.items.length, 1);
  assert.equal(response.items[0].stock.cantidadDisponible, 1);
});

test('P3 catálogo: sin colección Stocks se preserva el DTO del cliente', () => {
  const p = { id: 'demo', stock: { cantidadDisponible: 7 } };
  assert.equal(catalog.normalizarProducto(p), p);
});

test('P3 reporte: no exportar KPI sin datos como cero', () => {
  const data = {
    periodo: { desde: '2026-10-01', hasta: '2026-10-31', diasPeriodo: 30 },
    generadoEn: '2026-11-08T00:00:00Z',
    resumenEjecutivo: { totalOrdenes: 0 },
    kpis: [{ nombre: 'OTIF', valor: 0, unidad: '%', meta: 95, semaforo: 'Neutro', tieneDatos: false },
      { nombre: 'Tasa', valor: 7.5, unidad: '%', meta: 5, semaforo: 'Amarillo', tieneDatos: true }],
  };
  const csv = report.crearReporteKpisCsv(data);
  assert.ok(csv.includes('"OTIF","Sin datos"'));
  assert.ok(csv.includes('"Tasa","7.5"'));
  assert.ok(csv.includes('"2026-10-01"'));
});

test('P3 reporte: evita fórmulas CSV inyectadas por nombres externos', () => {
  const data = { periodo: { desde: 'a', hasta: 'b', diasPeriodo: 7 },
    generadoEn: '2026-11-08', resumenEjecutivo: { totalOrdenes: 1 },
    kpis: [{ nombre: '=HYPERLINK("x")', valor: 1, unidad: '%', meta: 5, semaforo: 'Rojo', tieneDatos: true }] };
  const csv = report.crearReporteKpisCsv(data);
  assert.ok(csv.includes("'=HYPERLINK"));
  assert.ok(!csv.includes('"=HYPERLINK'));
});

test('P3 UX: error 503 HTML de proxy no se muestra a usuario', async () => {
  const client = loadTs('src/api/client.ts', {}, {
    localStorage: { getItem: () => null },
    fetch: async () => ({ ok: false, status: 503, statusText: 'Service Unavailable',
      text: async () => '<html><body>trace: secret-stack</body></html>' }),
  });
  await assert.rejects(client.apiClient.get('/api/productos'), error => {
    assert.equal(error.status, 503);
    assert.ok(error.message.includes('temporalmente'));
    assert.ok(!error.message.includes('<html>'));
    assert.ok(!error.message.includes('secret-stack'));
    return true;
  });
});
