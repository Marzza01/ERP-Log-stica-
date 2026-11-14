#!/usr/bin/env node
// F267 — Aceptación HTTP STAGING; nunca usa Production ni imprime credenciales.
// El POST de checkout lleva un carrito VACÍO: no puede generar pago si
// la configuración se cambió por error. Con proveedor Disabled exige HTTP 503.
import { randomUUID } from 'node:crypto';

const origin = 'https://marketplaceerp-api-staging.onrender.com';
const statuses = {};
const evidence = {
  timestampUtc: new Date().toISOString(),
  endpoint: origin,
  scenario: 'F267-STAGING-AUTH-ROLES-READONLY-PAYMENTS-DISABLED',
  statuses,
  completed: false,
  operationsWithWrites: 0
};

async function request(label, path, {method = 'GET', token, data, headers = {}} = {}) {
  const target = new URL(path, origin);
  if (target.origin !== origin) throw new Error('Destino no permitido');
  const h = {Accept: 'application/json', ...headers};
  if (token) h.Authorization = 'Bearer ' + token;
  if (data !== undefined) h['Content-Type'] = 'application/json';
  const res = await fetch(target, {
    method, headers: h, redirect: 'manual',
    body: data === undefined ? undefined : JSON.stringify(data),
    signal: AbortSignal.timeout(25000)
  });
  statuses[label] = res.status;
  if (res.status >= 300 && res.status < 400)
    throw new Error(label + ' devolvió redirección inesperada');
  return res;
}

function check(label, res, expected) {
  if (res.status !== expected)
    throw new Error(label + ': esperaba ' + expected + ', obtuvo ' + res.status);
}

const dangerousPaymentSafePayload = {items: []};
const post = (label, token) => request(label, '/api/checkout/v2/comprar', {
  method: 'POST', token,
  headers: {'Idempotency-Key': 'F267-NO-COMPRA-' + randomUUID().replaceAll('-', '')},
  data: dangerousPaymentSafePayload
});

async function login(role, prefix) {
  const email = process.env[prefix + '_EMAIL'];
  const password = process.env[prefix + '_PASSWORD'];
  if (!email || !password)
    throw new Error('Faltan variables locales para el rol ' + role);
  const r = await request('login_' + role, '/api/auth/login', {
    method: 'POST', data: {email, password}
  });
  check('login_' + role, r, 200);
  const dto = await r.json();
  if (dto.role !== role || dto.rol !== role || typeof dto.token !== 'string')
    throw new Error('Rol o formato JWT inesperado: ' + role);
  return dto.token; // Solo memoria; nunca log ni archivos.
}

try {
  check('health', await request('health', '/health'), 200);
  check('ready', await request('ready', '/health/ready'), 200);
  check('admin_anonimo', await request('admin_anonimo',
    '/api/checkout/pendientes-conciliacion?limite=2'), 401);
  check('checkout_anonimo', await post('checkout_anonimo'), 401);

  if (process.argv.includes('--anonymous')) {
    console.log('F267_EVIDENCIA=' + JSON.stringify({
      ...evidence, completed: true, mode: 'anonymous-only',
      roleAcceptanceCompleted: false
    }));
    process.exit(0);
  }
  if (process.argv.length !== 2)
    throw new Error('Argumentos no permitidos');

  const admin = await login('Admin', 'F267_ADMIN');
  const buyer = await login('Comprador', 'F267_BUYER');
  const seller = await login('Vendedor', 'F267_SELLER');
  check('admin_queue', await request('admin_queue',
    '/api/checkout/pendientes-conciliacion?limite=2', {token:admin}), 200);

  const capabilities = await request('admin_recovery_capabilities',
    '/api/checkout/recuperacion/capacidades', {token:admin});
  check('admin_recovery_capabilities', capabilities, 200);
  const caps = await capabilities.json();
  if (caps.conciliacionQA !== false || caps.reembolsosQA !== false)
    throw new Error('Recuperación QA habilitada indebidamente en STAGING');

  check('buyer_admin_queue_forbidden', await request('buyer_admin_queue_forbidden',
    '/api/checkout/pendientes-conciliacion?limite=2', {token:buyer}), 403);
  check('seller_admin_queue_forbidden', await request('seller_admin_queue_forbidden',
    '/api/checkout/pendientes-conciliacion?limite=2', {token:seller}), 403);

  check('admin_checkout_forbidden', await post('admin_checkout_forbidden', admin), 403);
  check('seller_checkout_forbidden', await post('seller_checkout_forbidden', seller), 403);

  const paymentDisabled = await post('buyer_checkout_disabled', buyer);
  check('buyer_checkout_disabled', paymentDisabled, 503);
  const body = await paymentDisabled.json();
  if (body.tipo !== 'PagosDeshabilitados')
    throw new Error('El 503 no corresponde a PagosDeshabilitados');

  check('buyer_catalogo', await request('buyer_catalogo',
    '/api/productos?pagina=1&tamano=1', {token:buyer}), 200);
  evidence.completed = true;
  console.log('F267_EVIDENCIA=' + JSON.stringify({
    ...evidence, mode: 'staging-authenticated-three-roles',
    roleAcceptanceCompleted: true, recoveryQaDisabled: true
  }));
} catch (err) {
  console.error('F267_EVIDENCIA=' + JSON.stringify({
    ...evidence, error: err instanceof Error ? err.message : 'Error de validación'
  }));
  process.exitCode = 1;
}
