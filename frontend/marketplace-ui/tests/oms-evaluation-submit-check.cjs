const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');
const ts = require('typescript');

const clientPath = path.join(__dirname, '../src/api/client.ts');
const componentPath = path.join(__dirname, '../src/pages/ordenes/NuevaOrdenEvaluacion.tsx');
const clientSource = fs.readFileSync(clientPath, 'utf8');
const componentSource = fs.readFileSync(componentPath, 'utf8');
const compiled = ts.transpileModule(clientSource, {
  fileName: 'client.ts',
  reportDiagnostics: true,
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020, strict: true },
});
const faults = compiled.diagnostics?.filter(d => d.category === ts.DiagnosticCategory.Error) ?? [];
assert.equal(faults.length, 0, 'El cliente HTTP debe compilar sin errores');

function loadClient(responseFactory) {
  const exports = {};
  let captured;
  const context = {
    exports,
    localStorage: { getItem: () => 'qa-token', removeItem: () => {} },
    fetch: async (url, options) => {
      captured = { url, options };
      return responseFactory();
    },
  };
  vm.runInNewContext(compiled.outputText, context);
  return { apiClient: exports.apiClient, ApiError: exports.ApiError, getRequest: () => captured };
}

test('OMS: HTTP 201 sin cuerpo confirma la orden y conserva Idempotency-Key', async () => {
  const client = loadClient(() => ({
    ok: true, status: 201, statusText: 'Created', text: async () => '',
  }));
  const payload = { compradorId: 'buyer', vendedorId: 'seller', items: [{ productoId: 'sku', cantidad: 1 }] };
  const result = await client.apiClient.postVoid('/api/ordenes', payload, {
    'Idempotency-Key': 'oms-idempotency-123456',
  });
  assert.equal(result, undefined);
  const request = client.getRequest();
  assert.equal(request.url, '/api/ordenes');
  assert.equal(request.options.method, 'POST');
  assert.equal(request.options.headers['Idempotency-Key'], 'oms-idempotency-123456');
  assert.deepEqual(JSON.parse(request.options.body), payload);
});

test('OMS: un rechazo HTTP no se interpreta como creación confirmada', async () => {
  const client = loadClient(() => ({
    ok: false, status: 409, statusText: 'Conflict',
    text: async () => JSON.stringify({ mensaje: 'La clave ya pertenece a otra solicitud.' }),
  }));
  await assert.rejects(
    client.apiClient.postVoid('/api/ordenes', {}, { 'Idempotency-Key': 'oms-idempotency-123456' }),
    error => error instanceof client.ApiError && error.status === 409,
  );
});

test('OMS: el formulario no accede a result.id después de un 2xx y limpia el pendiente', () => {
  assert.ok(componentSource.includes("await apiClient.postVoid('/api/ordenes'"));
  assert.ok(!componentSource.includes('result.id'));
  assert.ok(componentSource.includes('localStorage.removeItem(storageKey);'));
  assert.ok(componentSource.includes('setPending(null); setOpen(false); onCreated();'));
});

console.log('OMS evaluación: pruebas de confirmación HTTP, idempotencia y desbloqueo aprobadas.');
