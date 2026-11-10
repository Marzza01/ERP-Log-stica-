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
// Checkout V1 comparte la comprobación de marcas pendientes con V2.
const v2Source = fs.readFileSync(path.join(__dirname, '../src/pages/checkout/carritoV2Qa.ts'), 'utf8');
const v2Compiled = ts.transpileModule(v2Source, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020, strict: true },
});
const v2Exports = {};
vm.runInNewContext(v2Compiled.outputText, { exports: v2Exports, localStorage });
const moduleExports = {};
vm.runInNewContext(compiled.outputText, {
  exports: moduleExports,
  localStorage,
  require: mod => {
    if (mod === './carritoV2Qa') return v2Exports;
    throw new Error('Módulo inesperado: ' + mod);
  },
  crypto: { randomUUID: () => '11111111-2222-4333-8444-555555555555' },
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
assert.equal(p.pendienteResuelto('Aprobada', 'Completado', null, 0, false), true);
assert.equal(p.pendienteResuelto('Pendiente', 'Incierto', null, 0, false), false);
assert.equal(p.pendienteResuelto('Cancelada', 'Rechazado', null, 1, false), false);
assert.equal(p.pendienteResuelto('Cancelada', 'Rechazado', null, 0, false), true);
assert.equal(p.pendienteResuelto('Cancelada', 'Aprobado', 'Finalizado', 0, false), true);
assert.equal(p.pendienteResuelto('Cancelada', 'CanceladoAntesEnvio', null, 0, false), true);
assert.equal(p.pendienteResuelto('Cancelada', 'CanceladoAntesEnvio', null, 0, true), false);
assert.equal(p.pendienteResuelto('Cancelada', 'CanceladoAntesEnvio', null, 2, false), false);
assert.equal(p.pendienteResuelto('Pendiente', 'CanceladoAntesEnvio', null, 0, false), false);
assert.equal(p.pendienteResuelto('Cancelada', 'CanceladoAntesEnvio', null, 0), false);
assert.equal(p.pendienteResuelto('Aprobada', 'Completado', null, 0), false);
assert.throws(() => p.guardarPendiente('uno@example.test', intento), /INTENTO_PREVIO_SIN_CONCILIAR/);
const actualizado = { ...intento, ordenId: uuid };
p.actualizarPendiente('uno@example.test', actualizado);
assert.equal(p.leerPendiente('uno@example.test').ordenId, uuid);
assert.throws(() => p.actualizarPendiente('uno@example.test',
  { ...actualizado, clave: 'CLAVE_DISTINTA' }), /ACTUALIZACION_V1_NO_AUTORIZADA/);
// Nunca sobrescribir un V1 ilegible ni iniciar V1 si existe V2.
storage.set('logimkt_checkout_b2c_v1:malformed@example.test:pendiente', '{corrupt');
assert.throws(() => p.guardarPendiente('malformed@example.test', intento), /INTENTO_PREVIO_SIN_CONCILIAR/);
assert.equal(storage.get('logimkt_checkout_b2c_v1:malformed@example.test:pendiente'), '{corrupt');
storage.set('logimkt_checkout_b2c_v2_qa:multi@example.test:pendiente', '{corrupt');
assert.throws(() => p.guardarPendiente('multi@example.test', intento), /INTENTO_PREVIO_SIN_CONCILIAR/);
assert.equal(storage.get('logimkt_checkout_b2c_v2_qa:multi@example.test:pendiente'), '{corrupt');
assert.throws(() => p.borrarPendiente('uno@example.test', 'CLAVE_DISTINTA'),
  /INTENTO_V1_CAMBIO_ENTRE_PESTANAS/);
assert.equal(p.leerPendiente('uno@example.test').clave, actualizado.clave);
p.borrarPendiente('uno@example.test', actualizado.clave);
assert.equal(p.leerPendiente('uno@example.test'), null);
console.log('Checkout: 31 aserciones de estado/idempotencia aprobadas.');
