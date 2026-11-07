/**
 * P1: actual Chromium UI test with a deliberately local/mock API.
 * No staging credentials, JWT secrets, third-party payment endpoints or
 * persistent databases. This is NOT evidence of real staging auth.
 */
import assert from 'node:assert/strict';
import { chromium } from 'playwright';
import { spawn } from 'node:child_process';
import { setTimeout as delay } from 'node:timers/promises';

const baseUrl = 'http://127.0.0.1:4175';
const vite = spawn(process.execPath, [
  './node_modules/vite/bin/vite.js', '--host', '127.0.0.1',
  '--port', '4175', '--strictPort',
], { stdio: ['ignore', 'pipe', 'pipe'], env: { ...process.env, VITE_CHECKOUT_HABILITADO: 'false' } });

let serverOutput = '';
for (const stream of [vite.stdout, vite.stderr]) stream.on('data', data => {
  serverOutput += String(data).slice(0, 800);
  serverOutput = serverOutput.slice(-3000);
});

async function awaitVite() {
  for (let attempts = 0; attempts < 80; attempts++) {
    if (vite.exitCode !== null) throw Error('Vite terminó inesperadamente: ' + serverOutput);
    try {
      const response = await fetch(baseUrl + '/login');
      if (response.ok && (await response.text()).includes('LOGIMKT')) return;
    } catch { /* Not yet listening. */ }
    await delay(250);
  }
  throw Error('Vite no inició: ' + serverOutput);
}

function token(role, lifetimeMs = 15 * 60 * 1000) {
  const now = Date.now();
  // A syntactically valid JWT payload, never signed or sent to a real API.
  const exp = Math.floor((now + lifetimeMs) / 1000);
  const body = Buffer.from(JSON.stringify({ role, exp })).toString('base64url');
  return { token: 'e30.' + body + '.local-ui-test-only', expiresAt: new Date(now + lifetimeMs).toISOString() };
}

function setupApi(page) {
  let protectedStatus = 200;
  const requests = [];
  const scenarios = { status: code => { protectedStatus = code; }, requests };
  return page.route(url => url.pathname.startsWith('/api/'), async route => {
    const req = route.request();
    const url = new URL(req.url());
    requests.push({ path: url.pathname, method: req.method() });
    assert.equal(url.hostname, '127.0.0.1', 'No external API request allowed');
    if (url.pathname === '/api/auth/login') {
      const body = req.postDataJSON();
      if (body.password !== 'ci-fixture-password') {
        return route.fulfill({ status: 401, contentType: 'application/json',
          body: JSON.stringify({ mensaje: 'Credenciales inválidas' }) });
      }
      const role = body.email.startsWith('seller') ? 'Vendedor'
        : body.email.startsWith('buyer') ? 'Comprador' : 'Admin';
      const jwt = token(role, body.email.startsWith('short') ? 2400 : undefined);
      const result = { ...jwt, role, rol: body.email.startsWith('mismatch') ? 'Vendedor' : role,
        usuario: role + ' CI' };
      return route.fulfill({ status: 200, contentType: 'application/json',
        body: JSON.stringify(result) });
    }
    if (protectedStatus !== 200) {
      return route.fulfill({ status: protectedStatus, contentType: 'application/json',
        body: JSON.stringify({ mensaje: 'Respuesta de prueba ' + protectedStatus }) });
    }
    // The mock has no financial KPI fixture: return a controlled server error,
    // which the Dashboard handles, rather than malformed 200 financial objects.
    if (url.pathname.startsWith('/api/kpis-dashboard')) {
      return route.fulfill({ status: 503, contentType: 'application/json',
        body: JSON.stringify({ mensaje: 'KPI no disponible en el fixture local' }) });
    }
    if (url.pathname.startsWith('/api/productos')) {
      return route.fulfill({ status: 200, contentType: 'application/json',
        body: JSON.stringify({ total: 0, items: [], pagina: 1 }) });
    }
    if (url.pathname.startsWith('/api/ordenes')) {
      return route.fulfill({ status: 200, contentType: 'application/json',
        body: JSON.stringify({ total: 0, items: [], pagina: 1 }) });
    }
    return route.fulfill({ status: 200, contentType: 'application/json',
      body: JSON.stringify({ resumenEjecutivo: { totalOrdenes: 0 }, kpis: [],
        meses: [], resumenAnual: { ventasTotalesAnuales: 0 }, items: [], total: 0 }) });
  }).then(() => scenarios);
}

async function signIn(page, email, password = 'ci-fixture-password') {
  await page.getByPlaceholder('nombre@ejemplo.com').fill(email);
  await page.locator('input[type=password]').fill(password);
  await page.getByRole('button', { name: 'Ingresar al sistema' }).click();
}

async function run() {
  await awaitVite();
  const browser = await chromium.launch({ headless: true, args: ['--no-sandbox'] });
  const failures = [];
  try {
    const page = await browser.newPage();
    page.on('pageerror', error => failures.push(error.message));
    const scenarios = await setupApi(page);

    await page.goto(baseUrl + '/dashboard');
    try {
      await page.waitForURL('**/login', { timeout: 12000 });
    } catch (error) {
      const content = await page.locator('body').innerText().catch(() => '<unavailable>');
      throw new Error('Initial guest redirect failed; current URL=' + page.url() +
        '; visible body=' + content.slice(0, 900) +
        '; errors=' + failures.slice(0, 5).join(' | ') +
        '; vite=' + serverOutput.slice(-500), { cause: error });
    }
    console.log('OK P1 Chromium: unauthenticated protected route -> /login');

    await signIn(page, 'admin-fixture@example.invalid', 'wrong-password');
    await page.getByText('Correo electrónico o contraseña incorrectos.').waitFor();
    assert.equal(await page.evaluate(() => localStorage.getItem('erp_token')), null);
    console.log('OK P1 Chromium: invalid login -> error; no local auth');

    await signIn(page, 'admin-fixture@example.invalid');
    await page.waitForURL('**/dashboard');
    const firstToken = await page.evaluate(() => localStorage.getItem('erp_token'));
    assert.ok(firstToken && firstToken.includes('.'));
    await page.reload();
    await page.waitForURL('**/dashboard');
    assert.equal(await page.evaluate(() => localStorage.getItem('erp_token')), firstToken);
    console.log('OK P1 Chromium: Admin login and persisted refresh');

    // A 403 is not a logout. A 401 on a protected endpoint is.
    scenarios.status(403);
    await page.goto(baseUrl + '/ordenes');
    await page.waitForTimeout(500);
    assert.equal(await page.evaluate(() => localStorage.getItem('erp_token')), firstToken);
    scenarios.status(401);
    await page.goto(baseUrl + '/ordenes?again=1');
    await page.waitForURL('**/login', { waitUntil: 'domcontentloaded' });
    await page.getByRole('heading', { name: 'Iniciar sesión' }).waitFor();
    assert.equal(await page.evaluate(() => localStorage.getItem('erp_token')), null);
    console.log('OK P1 Chromium: 403 preserves session; 401 clears');

    scenarios.status(200);
    await signIn(page, 'seller-fixture@example.invalid');
    await page.waitForURL('**/ordenes');
    assert.equal(await page.locator('.sidebar-nav').getByText('WMS / Bodega').count(), 0);
    await page.goto(baseUrl + '/wms');
    await page.getByRole('heading', { name: 'Acceso no autorizado' }).waitFor();
    await page.getByRole('button', { name: 'Volver al inicio permitido' }).click();
    await page.waitForURL('**/ordenes');
    await page.getByRole('button', { name: 'Cerrar sesión' }).click();
    await page.waitForURL('**/login');
    assert.equal(await page.evaluate(() => localStorage.getItem('erp_token')), null);
    console.log('OK P1 Chromium: Vendedor authorized home, role denial, logout');

    await signIn(page, 'buyer-fixture@example.invalid');
    await page.waitForURL('**/checkout');
    await page.goto(baseUrl + '/dashboard');
    await page.getByRole('heading', { name: 'Acceso no autorizado' }).waitFor();
    await page.getByRole('button', { name: 'Cerrar sesión' }).click();
    await page.waitForURL('**/login');
    console.log('OK P1 Chromium: Comprador has only checkout access');

    await signIn(page, 'short-fixture@example.invalid');
    await page.waitForURL('**/dashboard');
    try {
      await page.waitForFunction(
        () => location.pathname === '/login' &&
              localStorage.getItem('erp_token') === null,
        null, { timeout: 9000 }
      );
    } catch (error) {
      const diagnostic = await page.evaluate(() => ({
        path: location.pathname,
        expiresInMs: Date.parse(localStorage.getItem('erp_expires_at') || '') - Date.now(),
        sessionPresent: !!localStorage.getItem('erp_token'),
      })).catch(() => ({ unavailable: true }));
      throw new Error('JWT idle expiry assertion failed: ' +
        JSON.stringify(diagnostic) + ', browser errors: ' + failures.join(' | '),
        { cause: error });
    }
    console.log('OK P1 Chromium: expired JWT logs out without API response');

    // Valid token but an inconsistent server role must not create a session.
    await signIn(page, 'mismatch-fixture@example.invalid');
    await page.getByText('Respuesta de autenticación inválida del servidor.').waitFor();
    assert.equal(await page.evaluate(() => localStorage.getItem('erp_token')), null);
    console.log('OK P1 Chromium: inconsistent claims rejected');

    assert.ok(scenarios.requests.some(r => r.path === '/api/auth/login'));
    assert.equal(failures.length, 0, 'Unexpected browser errors: ' + failures.join(' | '));
    console.log('P1_BROWSER_EVIDENCE=' + JSON.stringify({
      mode: 'local-chromium-mocked-api', scenarios: 8, completed: true,
      stagingCredentialUsed: false, productionTouched: false,
    }));
  } finally {
    await browser.close();
  }
}

try { await run(); }
finally {
  vite.kill('SIGTERM');
  await delay(200);
}
