const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { pathToFileURL } = require('node:url');
const ts = require('typescript');

const root = path.resolve(__dirname, '..');

function loadTypescript(relativePath, globals = {}) {
  const source = fs.readFileSync(path.join(root, relativePath), 'utf8');
  const compiled = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
    reportDiagnostics: true,
  });
  assert.deepEqual(compiled.diagnostics ?? [], []);
  const module = { exports: {} };
  vm.runInNewContext(compiled.outputText, {
    module,
    exports: module.exports,
    require: () => { throw new Error('Unexpected runtime import'); },
    atob,
    Date,
    ...globals,
  }, { filename: relativePath });
  return module.exports;
}

function fakeToken(role, expSeconds) {
  return `e30.${Buffer.from(JSON.stringify({ role, exp: expSeconds })).toString('base64url')}.sig`;
}

function memoryStorage() {
  const values = new Map();
  return {
    getItem: key => values.get(key) ?? null,
    setItem: (key, value) => values.set(key, value),
    removeItem: key => values.delete(key),
    size: () => values.size,
  };
}

const sessions = loadTypescript('src/context/authSession.ts');

test('login real confirmado se conserva tras recarga y logout lo elimina', () => {
  const exp = Math.floor(Date.now() / 1000) + 900;
  const response = {
    token: fakeToken('Admin', exp), role: 'Admin', rol: 'Admin',
    usuario: 'Administrador', expiresAt: new Date(exp * 1000).toISOString(),
  };
  const storage = memoryStorage();
  const session = sessions.sessionFromLogin(response, 'ADMIN@EXAMPLE.COM');
  sessions.persistSession(storage, session);
  assert.equal(sessions.readStoredSession(storage).usuario.email, 'admin@example.com');
  assert.equal(sessions.readStoredSession(storage).usuario.rol, 'Admin');
  sessions.clearStoredSession(storage);
  assert.equal(storage.size(), 0);
  assert.equal(sessions.readStoredSession(storage), null);
});

test('expiración o respuesta incompleta nunca crea una sesión', () => {
  const future = Math.floor(Date.now() / 1000) + 900;
  const past = Math.floor(Date.now() / 1000) - 10;
  const base = { token: fakeToken('Admin', future), role: 'Admin', rol: 'Admin',
    usuario: 'Administrador', expiresAt: new Date(future * 1000).toISOString() };
  assert.throws(() => sessions.sessionFromLogin({ ...base, expiresAt: '' }, 'a@example.com'));
  assert.throws(() => sessions.sessionFromLogin({ ...base, role: 'Vendedor' }, 'a@example.com'));
  assert.throws(() => sessions.sessionFromLogin({ ...base, token: fakeToken('Admin', past) }, 'a@example.com'));
  const storage = memoryStorage();
  sessions.persistSession(storage, sessions.sessionFromLogin(base, 'a@example.com'));
  storage.setItem('erp_expires_at', new Date(past * 1000).toISOString());
  assert.equal(sessions.readStoredSession(storage), null);
  assert.equal(storage.size(), 0);
});

test('persistencia parcial se limpia si falla el almacenamiento', () => {
  const exp = Math.floor(Date.now() / 1000) + 900;
  const storage = memoryStorage();
  const guardedStorage = { ...storage, setItem(key, value) {
    if (key === 'erp_expires_at') throw new Error('storage unavailable');
    storage.setItem(key, value);
  } };
  const session = sessions.sessionFromLogin({ token: fakeToken('Admin', exp), role: 'Admin',
    rol: 'Admin', usuario: 'Administrador', expiresAt: new Date(exp * 1000).toISOString() }, 'a@example.com');
  assert.throws(() => sessions.persistSession(guardedStorage, session));
  assert.equal(storage.size(), 0);
});

test('middleware de API precede a la SPA sin rewrite externo directo', async () => {
  const config = JSON.parse(fs.readFileSync(path.join(root, 'vercel.json'), 'utf8'));
  assert.deepEqual(config.rewrites, [{ source: '/(.*)', destination: '/index.html' }]);
  const proxy = await import(pathToFileURL(path.join(root, 'middleware.js')).href);
  assert.equal(proxy.config.matcher, '/api/:path*');
  const apiHeaders = config.headers.find(entry => entry.source === '/api/:path*').headers;
  assert.equal(apiHeaders.find(header => header.key === 'Cache-Control').value, 'no-store');
});

async function withProxyEnvironment(environment, stagingOrigin, fakeFetch, action) {
  const priorEnvironment = process.env.VERCEL_ENV;
  const priorOrigin = process.env.API_STAGING_ORIGIN;
  const priorFetch = globalThis.fetch;
  if (environment === undefined) delete process.env.VERCEL_ENV;
  else process.env.VERCEL_ENV = environment;
  if (stagingOrigin === undefined) delete process.env.API_STAGING_ORIGIN;
  else process.env.API_STAGING_ORIGIN = stagingOrigin;
  globalThis.fetch = fakeFetch;
  try {
    const { default: proxy } = await import(pathToFileURL(path.join(root, 'middleware.js')).href);
    await action(proxy);
  } finally {
    if (priorEnvironment === undefined) delete process.env.VERCEL_ENV;
    else process.env.VERCEL_ENV = priorEnvironment;
    if (priorOrigin === undefined) delete process.env.API_STAGING_ORIGIN;
    else process.env.API_STAGING_ORIGIN = priorOrigin;
    globalThis.fetch = priorFetch;
  }
}

test('Preview sin destino exacto falla cerrado y nunca contacta Production', async () => {
  for (const origin of [undefined, 'https://marketplaceerp-api.onrender.com', 'https://other.invalid']) {
    await withProxyEnvironment('preview', origin, () => { throw new Error('fetch inesperado'); }, async proxy => {
      const response = await proxy(new Request('https://preview.vercel.app/api/auth/login', { method: 'POST' }));
      assert.equal(response.status, 503);
      assert.equal(response.headers.get('cache-control'), 'no-store');
    });
  }
});

test('Preview reenvía login al staging fijo y conserva método, cuerpo, JWT y 401', async () => {
  const calls = [];
  await withProxyEnvironment('preview', 'https://marketplaceerp-api-staging.onrender.com',
    async (url, options) => {
      calls.push({ url: url.toString(), options });
      return new Response(JSON.stringify({ mensaje: 'Credenciales inválidas' }),
        { status: 401, headers: { 'Content-Type': 'application/json', 'Set-Cookie': 'session=not-forwarded' } });
    }, async proxy => {
      const response = await proxy(new Request('https://preview.vercel.app/api/auth/login?source=preview', {
        method: 'POST', headers: { Authorization: 'Bearer test-only', Cookie: 'vercel-session=test-only',
          'Content-Type': 'application/json' }, body: JSON.stringify({ email: 'a@example.com' }),
      }));
      assert.equal(response.status, 401);
      assert.equal(response.headers.get('cache-control'), 'no-store');
      assert.equal(response.headers.get('set-cookie'), null);
      assert.equal((await response.json()).mensaje, 'Credenciales inválidas');
    });
  assert.equal(calls.length, 1);
  assert.equal(calls[0].url, 'https://marketplaceerp-api-staging.onrender.com/api/auth/login?source=preview');
  assert.equal(calls[0].options.method, 'POST');
  assert.equal(calls[0].options.headers.get('authorization'), 'Bearer test-only');
  assert.equal(calls[0].options.headers.get('cookie'), null);
  assert.equal(JSON.parse(new TextDecoder().decode(calls[0].options.body)).email, 'a@example.com');
});

test('Production usa Render Production y conserva respuestas 500 y 204', async () => {
  const urls = [];
  await withProxyEnvironment('production', 'https://marketplaceerp-api-staging.onrender.com',
    async url => {
      urls.push(url.toString());
      return urls.length === 1 ? new Response('fallo', { status: 500 }) : new Response(null, { status: 204 });
    }, async proxy => {
      assert.equal((await proxy(new Request('https://erp.vercel.app/api/ordenes'))).status, 500);
      assert.equal((await proxy(new Request('https://erp.vercel.app/api/ordenes'))).status, 204);
    });
  assert.deepEqual(urls, [
    'https://marketplaceerp-api.onrender.com/api/ordenes',
    'https://marketplaceerp-api.onrender.com/api/ordenes',
  ]);
});

test('el health del mismo origen llega a Render y no a la SPA', async () => {
  const calls = [];
  await withProxyEnvironment('production', undefined, async url => {
    calls.push(url.toString());
    return new Response(JSON.stringify({ status: 'healthy', servicio: 'MarketplaceERP.API' }),
      { status: 200, headers: { 'Content-Type': 'application/json' } });
  }, async proxy => {
    const response = await proxy(new Request('https://erp.vercel.app/api/health'));
    assert.equal(response.status, 200);
    assert.equal(response.headers.get('content-type'), 'application/json');
    assert.equal((await response.json()).servicio, 'MarketplaceERP.API');
  });
  assert.deepEqual(calls, ['https://marketplaceerp-api.onrender.com/health']);
});

function makeClient(fetchImpl, token = null) {
  const storage = memoryStorage();
  if (token) storage.setItem('erp_token', token);
  const client = loadTypescript('src/api/client.ts', {
    localStorage: storage,
    window: { location: { pathname: '/dashboard', href: '' } },
    fetch: fetchImpl,
  });
  return { client, storage };
}

test('login POST conserva ruta, método, cuerpo y código de respuesta', async () => {
  const calls = [];
  const { client } = makeClient(async (url, options) => {
    calls.push({ url, options });
    return new Response(JSON.stringify({ token: 'server-token' }), { status: 200 });
  });
  const response = await client.apiClient.post('/api/auth/login', { email: 'a@example.com', password: 'ficticia' });
  assert.equal(client.API_BASE, '');
  assert.equal(calls[0].url, '/api/auth/login');
  assert.equal(calls[0].options.method, 'POST');
  assert.equal(calls[0].options.headers['Content-Type'], 'application/json');
  assert.equal(JSON.parse(calls[0].options.body).email, 'a@example.com');
  assert.equal(response.token, 'server-token');
});

test('401 de login no cierra sesión global; 401 protegido sí la invalida', async () => {
  const { client } = makeClient(async () => new Response(JSON.stringify({ mensaje: 'No autorizado' }), { status: 401 }));
  let unauthorized = 0;
  client.setUnauthorizedHandler(() => { unauthorized++; });
  await assert.rejects(client.apiClient.post('/api/auth/login', {}), error => error.status === 401);
  assert.equal(unauthorized, 0);
  await assert.rejects(client.apiClient.get('/api/usuarios'), error => error.status === 401);
  assert.equal(unauthorized, 1);
});

test('403 informa falta de permisos sin cerrar sesión', async () => {
  const { client, storage } = makeClient(async () => new Response('Forbidden', { status: 403 }), 'token-ficticio');
  let forbidden = 0;
  client.setForbiddenHandler(() => { forbidden++; });
  await assert.rejects(client.apiClient.get('/api/usuarios'), error => error.status === 403);
  assert.equal(forbidden, 1);
  assert.equal(storage.getItem('erp_token'), 'token-ficticio');
});

test('métodos, cabeceras y códigos HTTP no se convierten en éxito local', async () => {
  const calls = [];
  const { client } = makeClient(async (url, options) => {
    calls.push({ url, options });
    if (options.method === 'PUT') return new Response('Error interno', { status: 500 });
    return new Response(null, { status: 204 });
  }, 'token-ficticio');
  await assert.rejects(client.apiClient.put('/api/ordenes/123/estado', { estado: 'Enviada' }),
    error => error.status === 500);
  assert.equal(calls[0].url, '/api/ordenes/123/estado');
  assert.equal(calls[0].options.method, 'PUT');
  assert.equal(calls[0].options.headers.Authorization, 'Bearer token-ficticio');
  assert.equal(JSON.parse(calls[0].options.body).estado, 'Enviada');
  assert.equal(await client.apiClient.delete('/api/ordenes/123'), undefined);
  assert.equal(calls[1].options.method, 'DELETE');
});
