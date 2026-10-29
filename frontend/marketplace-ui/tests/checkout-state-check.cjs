// Node built-in assertions: independent from React/browser, uses project's TypeScript dependency.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const ts = require('typescript');

const src = fs.readFileSync(path.join(__dirname, '../src/pages/checkout/checkoutState.ts'), 'utf8');
const compiled = ts.transpileModule(src, {
  fileName: 'checkoutState.ts',
  reportDiagnostics: true,
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020, strict: true },
});
const faults = compiled.diagnostics?.filter(d => d.category === ts.DiagnosticCategory.Error) ?? [];
assert.equal(faults.length, 0, 'El módulo TypeScript debe ser válido');
const storage = new Map();
const localStorage = {
  getItem: key => storage.has(key) ? storage.get(key) : null,
  setItem: (key, value) => { storage.set(key, String(value)); },
  removeItem: key => { storage.delete(key); },
};
const moduleExports = {};
vm.runInNewContext(compiled.outputText, {
  exports: moduleExports, localStorage, crypto: { randomUUID: () => '11111111-2222-4333-8444-555555555555' },
});
const p = moduleExports;
const uuid = '1ee729b9-614b-4a29-b3aa-d51d3f5079bf';
assert.equal(p.esGuid(uuid), true);
assert.equal(p.esGuid('mock-1'), false);
assert.equal(p.nuevaClaveIdempotencia(), '11111111222243338444555555555555');
assert.equal(p.leerPendiente('uno@example.test'), null);
const intento = { clave: '12345678', productoId: uuid, productoNombre: 'Producto', cantidad: 3, creadoUtc: '2026-10-29T00:00:00Z' };
p.guardarPendiente('uno@example.test', intento);
assert.equal(p.leerPendiente('uno@example.test').cantidad, 3);
assert.equal(p.leerPendiente('dos@example.test'), null);
assert.equal(p.recordarOrden('uno@example.test', uuid).length, 1);
assert.equal(p.recordarOrden('uno@example.test', uuid).length, 1);
assert.equal(p.leerOrdenes('dos@example.test').length, 0);
assert.equal(p.pendienteResuelto('Aprobada', 'Completado', null, 0), true);
assert.equal(p.pendienteResuelto('Pendiente', 'Incierto', null, 0), false);
assert.equal(p.pendienteResuelto('Cancelada', 'Rechazado', null, 1), false);
assert.equal(p.pendienteResuelto('Cancelada', 'Rechazado', null, 0), true);
assert.equal(p.pendienteResuelto('Cancelada', 'Aprobado', 'Finalizado', 0), true);
p.borrarPendiente('uno@example.test');
assert.equal(p.leerPendiente('uno@example.test'), null);
console.log('Checkout: 14 verificaciones de estado/idempotencia aprobadas.');
