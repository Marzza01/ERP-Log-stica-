#!/usr/bin/env node
/**
 * P1 real Render+Neon staging read-only HTTP acceptance.
 * No credentials or JWT are ever printed or written to disk.
 * GET-only after the initial login. Never requests Production URLs.
 */
const origin = 'https://marketplaceerp-api-staging.onrender.com';
if (process.argv.includes('--help')) {
  console.log('Uso: P1_ADMIN_EMAIL/P1_ADMIN_PASSWORD (o P1_ADMIN_TOKEN) node scripts/p1-staging-auth-readonly.mjs');
  console.log('Solo consultas seguras en Render staging. No escribe datos, no imprime credenciales.');
  process.exit(0);
}
if (process.argv.length !== 2) throw new Error('No se permiten argumentos adicionales.');
const email = process.env.P1_ADMIN_EMAIL;
const password = process.env.P1_ADMIN_PASSWORD;
const suppliedToken = process.env.P1_ADMIN_TOKEN;
if (!suppliedToken && (email !== 'admin.evaluacion@erp.example' || !password))
  throw new Error('Faltan credenciales privadas de Admin staging, solo en variables locales.');
if (suppliedToken && (email || password))
  throw new Error('Usar P1_ADMIN_TOKEN o email+password, no ambos.');

const statuses = {};
async function request(label, path, init = {}) {
  const url = new URL(path, origin);
  if (url.origin !== origin) throw new Error('Destino rechazado: solo Render staging.');
  const res = await fetch(url, {
    ...init, redirect: 'manual', signal: AbortSignal.timeout(30000),
    headers: { 'Accept': 'application/json', ...(init.headers ?? {}) }
  });
  if (res.status >= 300 && res.status < 400)
    throw new Error(label + ': redirección inesperada, no seguirla.');
  statuses[label] = res.status;
  return res;
}
function requireStatus(label, response, code) {
  if (response.status !== code)
    throw new Error(label + ': esperado HTTP ' + code + ', recibido ' + response.status);
  console.log('OK ' + label + ': HTTP ' + response.status);
}

try {
  requireStatus('health', await request('health', '/health'), 200);
  requireStatus('readiness', await request('readiness', '/health/ready'), 200);
  requireStatus('compras anonimo',
    await request('compras anonimo', '/api/compras/po'), 401);

  let token = suppliedToken;
  if (!token) {
    const loginResponse = await request('login Admin staging', '/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password })
    });
    requireStatus('login Admin staging', loginResponse, 200);
    const login = await loginResponse.json();
    if (!['Admin'].includes(login.role) || !login.token ||
        login.rol !== 'Admin' || login.token.split('.').length !== 3)
      throw new Error('La autenticación no devolvió JWT y rol Admin esperados.');
    token = login.token;
  }
  const headers = { Authorization: 'Bearer ' + token };
  const securedGets = [
    ['compras Admin', '/api/compras/po'],
    ['catalogo Admin', '/api/productos?pagina=1&tamano=1'],
    ['ordenes Admin', '/api/ordenes?pagina=1&tamano=1'],
  ];
  for (const [label, path] of securedGets) {
    requireStatus(label, await request(label, path, { headers }), 200);
  }

  const tokenParts = token.split('.');
  if (tokenParts.length !== 3 || !tokenParts[2])
    throw new Error('JWT recibido con firma no reconocible.');
  const signature = tokenParts[2];
  tokenParts[2] = (signature[0] === 'A' ? 'B' : 'A') + signature.slice(1);
  const tampered = tokenParts.join('.');
  requireStatus('JWT adulterado rechazado',
    await request('JWT adulterado rechazado', '/api/compras/po',
      { headers: { Authorization: 'Bearer ' + tampered } }), 401);

  console.log('P1_AUTH_EVIDENCE=' + JSON.stringify({
    timestamp: new Date().toISOString(), environment: 'render-neon-staging',
    mode: 'readonly-authenticated-http', statuses,
    completed: true, operationsWithWrites: 0,
  }));
} catch (err) {
  console.error('P1_AUTH_EVIDENCE=' + JSON.stringify({
    timestamp: new Date().toISOString(), environment: 'render-neon-staging',
    mode: 'readonly-authenticated-http', statuses,
    completed: false, error: err instanceof Error ? err.message : 'unknown',
  }));
  process.exitCode = 1;
}
