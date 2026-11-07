/**
 * P1 acceptance: real Chromium UI + real .NET JWT API + disposable PostgreSQL.
 * All services must be localhost. No fake roles/tokens and no external writes.
 * GitHub CI generates one-time Admin credentials and does not print them.
 */
import assert from 'node:assert/strict';
import { chromium } from 'playwright';
import { spawn } from 'node:child_process';
import { setTimeout as delay } from 'node:timers/promises';

const origin = 'http://127.0.0.1:4176';
const backend = 'http://127.0.0.1:5000';
const email = process.env.ADMIN_EMAIL;
const password = process.env.ADMIN_PASSWORD;
if (process.env.CI !== 'true' || !email?.endsWith('@erp.invalid') ||
    !password || password.length < 20)
  throw new Error('Solo se admite CI con credenciales efímeras en PostgreSQL local.');

async function ready(url, count = 100) {
  for (let i = 0; i < count; i++) {
    try {
      const response = await fetch(url);
      if (response.ok) return;
    } catch { /* service starting */ }
    await delay(250);
  }
  throw Error('Servicio de CI no disponible: ' + url);
}

let browser;
const vite = spawn(process.execPath, [
  './node_modules/vite/bin/vite.js', '--host', '127.0.0.1', '--port', '4176',
  '--strictPort',
], { stdio: 'ignore', env: { ...process.env, VITE_CHECKOUT_HABILITADO: 'false' } });

try {
  await ready(backend + '/health/ready');
  await ready(origin + '/login');
  browser = await chromium.launch({ headless: true, args: ['--no-sandbox'] });
  const page = await browser.newPage();
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  const requests = [];
  page.on('request', req => {
    const u = new URL(req.url());
    if (u.pathname.startsWith('/api/')) {
      assert.equal(u.hostname, '127.0.0.1', 'API should stay localhost');
      requests.push({ method: req.method(), path: u.pathname });
    }
  });

  await page.goto(origin + '/dashboard');
  await page.waitForURL('**/login', { timeout: 15000 });

  const anonStatus = await page.evaluate(async () => {
    const response = await fetch('/api/compras/po', { credentials: 'omit' });
    return response.status;
  });
  assert.equal(anonStatus, 401);
  console.log('OK P1 local full-stack: browser unauthenticated => 401');

  await page.getByPlaceholder('nombre@ejemplo.com').fill(email);
  await page.locator('input[type=password]').fill('invalid-test-password');
  await page.getByRole('button', { name: 'Ingresar al sistema' }).click();
  await page.getByText('Correo electrónico o contraseña incorrectos.').waitFor();
  assert.equal(await page.evaluate(() => localStorage.getItem('erp_token')), null);

  await page.locator('input[type=password]').fill(password);
  await page.getByRole('button', { name: 'Ingresar al sistema' }).click();
  await page.waitForURL('**/dashboard', { timeout: 15000 });
  await page.getByRole('heading', { name: 'Dashboard Ejecutivo' }).waitFor();
  const token = await page.evaluate(() => localStorage.getItem('erp_token'));
  assert.ok(token && token.length > 50, 'Real signed JWT expected');

  const protectedStatus = await page.evaluate(async () => {
    const jwt = localStorage.getItem('erp_token');
    return (await fetch('/api/compras/po', {
      headers: { Authorization: 'Bearer ' + jwt },
    })).status;
  });
  assert.equal(protectedStatus, 200);
  console.log('OK P1 local full-stack: Admin real JWT and authorized purchase GET');

  await page.reload();
  await page.waitForURL('**/dashboard', { timeout: 15000 });
  assert.equal(await page.evaluate(() => localStorage.getItem('erp_token')), token);
  await page.getByRole('button', { name: 'Cerrar sesión' }).click();
  await page.waitForURL('**/login', { timeout: 15000 });
  assert.equal(await page.evaluate(() => localStorage.getItem('erp_token')), null);
  assert.equal(errors.length, 0, 'Browser errors: ' + errors.join(' | '));
  assert.ok(requests.some(r => r.path === '/api/auth/login' && r.method === 'POST'));
  console.log('P1_LOCAL_REAL_E2E=' + JSON.stringify({
    completed: true, browser: 'chromium', backend: 'actual-.NET-8',
    database: 'disposable-local-postgresql', role: 'Admin',
    negativeLogin: 'passed', authorizedGet: 'passed', refresh: 'passed',
    logout: 'passed', stagingCredentialUsed: false, productionTouched: false,
  }));
} finally {
  if (browser) await browser.close();
  vite.kill('SIGTERM');
}
