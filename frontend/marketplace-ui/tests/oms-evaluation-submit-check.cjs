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
      return responseFactory(url, options);
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

test('OMS: recupera el ID persistido después de HTTP 201 sin cuerpo', async () => {
  const responses = [
    { ok: true, status: 201, statusText: 'Created', text: async () => '' },
    { ok: true, status: 200, statusText: 'OK', text: async () => JSON.stringify({
      id: 'f70ef5ff-fcd5-4f2d-aefb-522eb1b1777d',
      numeroOrden: 'TEST-20261116-f70ef5ff',
      estado: 'Pendiente',
      montoTotal: 33.25,
    }) },
  ];
  const requests = [];
  const client = loadClient((url, options) => {
    requests.push({ url, options });
    return responses.shift();
  });
  const payload = { compradorId: 'buyer', vendedorId: 'seller', items: [{ productoId: 'sku', cantidad: 1 }] };
  const headers = { 'Idempotency-Key': 'oms-idempotency-123456' };

  await client.apiClient.postVoid('/api/ordenes', payload, headers);
  const recovered = await client.apiClient.post('/api/ordenes/evaluacion/resultado', payload, headers);

  assert.equal(recovered.id, 'f70ef5ff-fcd5-4f2d-aefb-522eb1b1777d');
  assert.equal(requests.length, 2);
  assert.equal(requests[0].url, '/api/ordenes');
  assert.equal(requests[1].url, '/api/ordenes/evaluacion/resultado');
  for (const request of requests) {
    assert.equal(request.options.headers['Idempotency-Key'], headers['Idempotency-Key']);
    assert.deepEqual(JSON.parse(request.options.body), payload);
  }
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

test('OMS: el formulario solo limpia el pendiente tras recuperar y validar el ID', () => {
  assert.ok(componentSource.includes("await apiClient.postVoid('/api/ordenes'"));
  assert.ok(componentSource.includes("apiClient.post<ResultadoOrden>('/api/ordenes/evaluacion/resultado'"));
  assert.ok(componentSource.includes('if (!esGuid(result?.id)'));
  assert.ok(componentSource.includes('localStorage.removeItem(storageKey);'));
  assert.ok(componentSource.includes('setPending(null); setOpen(false); setLastCreated(result); onCreated();'));
  assert.ok(componentSource.indexOf('if (!esGuid(result?.id)') < componentSource.indexOf('localStorage.removeItem(storageKey);'));
});

console.log('OMS evaluación: pruebas de confirmación HTTP, idempotencia y desbloqueo aprobadas.');
