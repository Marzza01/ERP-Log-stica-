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

test('P3 EAN opcional: contrato JSON preserva NULL sin inventar código de barras', async () => {
  const calls = [];
  const api = loadTs('src/api/productos.ts', {
    apiClient: {
      post: async (url, payload) => { calls.push({ url, payload }); return payload; },
      get: async () => ({ items: [{ id: 'g', codigoEAN: null }], total: 1, pagina: 1, tamano: 8 }),
    },
  });
  const request = {
    sku: 'QA-NO-EAN', nombre: 'Artículo sin código verificado',
    descripcion: '', categoriaNombre: 'Prueba', subcategoria: '',
    precioB2C: 12.5, precioB2B: 11.25, stockMinimo: 1,
    proveedorNombre: 'QA', paisOrigen: 'SV', pesoKg: 1,
    dimensionesCm: '', unidadMedida: 'unidad', estadoProducto: 'Activo',
    estaPublicado: true,
    vendedorId: '713b54af-e490-4626-a025-65197558a9cb',
    categoriaId: '2576e782-dcdd-4a8b-88d3-e9d7d8994ff2',
  };
  await api.productosApi.crear(request);
  assert.equal(calls.length, 1);
  assert.equal(calls[0].url, '/api/productos');
  assert.equal(calls[0].payload.codigoEAN, null);
  assert.equal(calls[0].payload.vendedorId, request.vendedorId);
  assert.equal(calls[0].payload.categoriaId, request.categoriaId);
  assert.throws(() => api.productosApi.crear({ ...request, vendedorId: '' }),
    /vendedor activo y una categoría reales/);
  assert.throws(() => api.productosApi.crear({ ...request,
    vendedorId: '11111111-1111-1111-1111-111111111111' }),
    /vendedor activo y una categoría reales/);
  assert.throws(() => api.productosApi.crear({ ...request,
    categoriaId: '22222222-2222-2222-2222-222222222222' }),
    /vendedor activo y una categoría reales/);
  assert.throws(() => api.productosApi.crear({ ...request,
    categoriaId: '00000000-0000-0000-0000-000000000000' }),
    /vendedor activo y una categoría reales/);
  assert.equal(calls.length, 1);
  assert.equal(calls[0].payload.precioBase, request.precioB2C);
  assert.equal(calls[0].payload.stockInicial, 0);
  const consulta = await api.productosApi.listar();
  assert.equal(consulta.items[0].codigoEAN, null);
  assert.equal(catalog.normalizarProducto({ id: 'g', codigoEAN: null }).codigoEAN, null);
});

test('P3 creación de producto: las opciones proceden de Core y nunca de GUID inventados', async () => {
  const urls = [];
  const references = {
    vendedores: [{ id: '713b54af-e490-4626-a025-65197558a9cb', nombre: 'Vendedor real QA' }],
    categorias: [{ id: '2576e782-dcdd-4a8b-88d3-e9d7d8994ff2', nombre: 'Categoría real QA' }],
  };
  const api = loadTs('src/api/productos.ts', {
    apiClient: {
      get: async url => { urls.push(url); return references; },
    },
  });
  const selected = await api.productosApi.opcionesAlta();
  assert.equal(urls.length, 1);
  assert.equal(urls[0], '/api/productos/opciones-alta');
  assert.equal(selected.vendedores[0].id, references.vendedores[0].id);
  assert.equal(selected.categorias[0].id, references.categorias[0].id);
  const source = fs.readFileSync(
    path.join(__dirname, '../src/pages/productos/ProductosPage.tsx'), 'utf8');
  assert.ok(source.includes('productosApi.opcionesAlta()'));
  assert.ok(source.includes('opcionesAlta.vendedores'));
  assert.ok(source.includes('opcionesAlta.categorias'));
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
