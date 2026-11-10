// QA puro: sin render de React, red, API, compras ni pagos.
// TypeScript del sandbox transpila en VM contra localStorage falso.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const ts = require('typescript');

const file = path.join(__dirname, '../src/pages/checkout/carritoV2Qa.ts');
const code = fs.readFileSync(file, 'utf8');
const compiled = ts.transpileModule(code, {
  fileName: 'carritoV2Qa.ts',
  compilerOptions: {
    module: ts.ModuleKind.CommonJS,
    target: ts.ScriptTarget.ES2020,
    strict: true,
  },
  reportDiagnostics: true,
});
assert.equal(compiled.diagnostics.filter(d => d.category === ts.DiagnosticCategory.Error).length, 0);
const store = new Map();
const localStorage = {
  getItem: k => store.has(k) ? store.get(k) : null,
  setItem: (k, v) => { store.set(k, String(v)); },
  removeItem: k => { store.delete(k); },
};
const exportsQa = {};
vm.runInNewContext(compiled.outputText, { exports: exportsQa, localStorage });
const qa = exportsQa;

const a = '11111111-1111-4111-8111-111111111111';
const b = '22222222-2222-4222-8222-222222222222';
const c = '33333333-3333-4333-8333-333333333333';
const seller = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const seller2 = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const catalogo = [
  { id: a, vendedorId: seller, precioB2C: 8, moneda: 'USD', estaPublicado: true, estadoProducto: 'Activo' },
  { id: b, vendedorId: seller, precioB2C: 12, moneda: 'USD', estaPublicado: true, estadoProducto: 'Activo' },
];
const entrada = [{ productoId: b, cantidad: 1 }, { productoId: a, cantidad: 2 }, { productoId: b, cantidad: 2 }];
const normal = qa.normalizarCarritoV2(entrada, catalogo);
let checks = 0;
function ok(fn) { fn(); checks++; }
function falla(fn, codigo) {
  ok(() => assert.throws(fn, e => e.message.includes(codigo)));
}
ok(() => assert.equal(normal.length, 2));
ok(() => assert.equal(normal[0].productoId, a));
ok(() => assert.equal(normal[1].cantidad, 3));
ok(() => assert.equal(entrada[0].cantidad, 1)); // Inmutabilidad
ok(() => assert.equal(qa.productoDisponibleV2(catalogo[0]), true));
ok(() => assert.equal(qa.productoDisponibleV2({ ...catalogo[0], id: 'demo-prod-1' }), false));
ok(() => assert.equal(qa.productoDisponibleV2({ ...catalogo[0], estaPublicado: false }), false));
ok(() => assert.equal(qa.productoDisponibleV2({ ...catalogo[0], moneda: 'EUR' }), false));
falla(() => qa.normalizarCarritoV2([{ productoId: a, cantidad: 2 }], catalogo), 'CARRITO_V2_INVALIDO');
falla(() => qa.normalizarCarritoV2([{ productoId: a, cantidad: 1 }, { productoId: a, cantidad: 2 }], catalogo), 'CARRITO_V2_SKU_FUERA_LIMITE');
falla(() => qa.normalizarCarritoV2([{ productoId: a, cantidad: 9999 }, { productoId: a, cantidad: 1 }, { productoId: b, cantidad: 1 }], catalogo), 'CARRITO_V2_CANTIDAD_EXCEDIDA');
falla(() => qa.normalizarCarritoV2([{ productoId: a, cantidad: 1 }, { productoId: b, cantidad: -1 }], catalogo), 'LINEA_V2_NO_APTA');
falla(() => qa.normalizarCarritoV2([{ productoId: a, cantidad: 1 }, { productoId: c, cantidad: 1 }], catalogo), 'LINEA_V2_NO_APTA');
falla(() => qa.normalizarCarritoV2([{ productoId: a, cantidad: 1 }, { productoId: b, cantidad: 1 }], [...catalogo, { ...catalogo[0] }]), 'CATALOGO_V2_ID_DUPLICADO');
falla(() => qa.normalizarCarritoV2([{ productoId: a, cantidad: 1 }, { productoId: b, cantidad: 1 }], [catalogo[0], { ...catalogo[1], vendedorId: seller2 }]), 'CARRITO_V2_MULTI_VENDEDOR');

const mapa = new Map([[a, catalogo[0]], [b, catalogo[1]]]);
const demasiados = Array.from({ length: 21 }, (_, i) => {
  const id = `10000000-0000-4000-8000-${String(i + 1).padStart(12, '0')}`;
  mapa.set(id, { ...catalogo[0], id });
  return { productoId: id, cantidad: 1 };
});
falla(() => qa.normalizarCarritoV2(demasiados, [...mapa.values()]), 'CARRITO_V2_SKU_FUERA_LIMITE');
falla(() => qa.normalizarCarritoV2(Array(201).fill({ productoId: a, cantidad: 1 }), catalogo), 'CARRITO_V2_INVALIDO');

const exact = qa.payloadV2(normal);
ok(() => assert.equal(JSON.stringify(exact), JSON.stringify({ items: [{ productoId: a, cantidad: 2 }, { productoId: b, cantidad: 3 }] })));
ok(() => assert.equal(Object.keys(exact.items[0]).sort().join(','), 'cantidad,productoId'));
falla(() => qa.payloadV2(entrada), 'CARRO_NO_NORMALIZADO');

const p = {
  clave: 'LLAVE_SEGURA_V2_12345',
  items: normal,
  creadoUtc: '2026-11-09T09:00:00.000Z',
};
ok(() => assert.equal(qa.hayOperacionPendiente('comprador@example.test'), false));
qa.guardarPendienteV2('comprador@example.test', p);
ok(() => assert.equal(qa.hayOperacionPendiente('comprador@example.test'), true));
ok(() => assert.equal(qa.leerPendienteV2('COMPRADOR@example.test').clave, p.clave));
falla(() => qa.guardarPendienteV2('comprador@example.test', p), 'INTENTO_PREVIO_SIN_CONCILIAR');
ok(() => assert.equal(qa.leerPendienteV2('otro@example.test'), null));

// Una operación V1 incierta NO debe desbloquearse al utilizar la UX V2.
store.set('logimkt_checkout_b2c_v1:otro@example.test:pendiente', '{malformado');
ok(() => assert.equal(qa.hayOperacionPendiente('otro@example.test'), true));
falla(() => qa.guardarPendienteV2('otro@example.test', p), 'INTENTO_PREVIO_SIN_CONCILIAR');

ok(() => assert.equal(qa.hayOperacionPendiente(''), true));
// Fallo de almacenamiento bloquea la emisión de un POST.
const out = {};
vm.runInNewContext(compiled.outputText, {
  exports: out,
  localStorage: { getItem() { throw new Error('storage blocked'); }, setItem() { throw new Error('storage blocked'); } },
});
ok(() => assert.equal(out.hayOperacionPendiente('cliente@example.test'), true));
falla(() => out.guardarPendienteV2('cliente@example.test', p), 'INTENTO_PREVIO_SIN_CONCILIAR');

console.log(`Carrito V2 QA: ${checks} controles de modelo/compatibilidad aprobados; sin operaciones financieras.`);
