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
    if (url.pathname === '/api/productos' || url.pathname === '/api/productos/buscar') {
      const pagina = Number(url.searchParams.get('pagina') || 1);
      const tamano = Number(url.searchParams.get('tamano') || 50);
      const query = (url.searchParams.get('q') || '').toLowerCase();
      const items = Array.from({ length: 10000 }, (_, i) => ({
        id: String(i + 1), sku: 'SKU-' + String(i + 1).padStart(5, '0'),
        nombre: 'Producto ' + (i + 1), categoriaNombre: 'General', stockMinimo: 3,
        stocks: [{ cantidadTotal: 8, cantidadReservada: 2, cantidadBloqueada: 1 }],
      })).filter(x => !query || x.sku.toLowerCase().includes(query) || x.nombre.toLowerCase().includes(query));
      return route.fulfill({ status: 200, contentType: 'application/json',
        body: JSON.stringify({ total: items.length, pagina, tamano,
          items: items.slice((pagina - 1) * tamano, pagina * tamano) }) });
    }
    if (url.pathname === '/api/ordenes/mis-ordenes') {
      // Servidor simulado entrega solo registros propios; el cliente no pasa vendedorId.
      return route.fulfill({ status: 200, contentType: 'application/json',
        body: JSON.stringify({ total: 1, pagina: 1, tamano: 20, ordenes: [{
          id: '11111111-1111-4111-8111-111111111111',
          numeroOrden: 'F104-OWN-A', estado: 'Pendiente', tipoCliente: 'B2C',
          montoTotal: 15, moneda: 'USD', fechaCreacion: '2026-11-10T12:00:00Z',
          totalItems: 1
        }] }) });
    }
    if (url.pathname === '/api/ordenes/mis-ordenes/11111111-1111-4111-8111-111111111111') {
      return route.fulfill({ status: 200, contentType: 'application/json',
        body: JSON.stringify({
          id: '11111111-1111-4111-8111-111111111111',
          numeroOrden: 'F104-OWN-A', estado: 'Pendiente', tipoCliente: 'B2C',
          montoTotal: 15, moneda: 'USD', fechaCreacion: '2026-11-10T12:00:00Z',
          items: [{ productoId: '22222222-2222-4222-8222-222222222222',
            sku: 'SKU-OWN-A', nombre: 'Producto propio A', cantidad: 1, precioUnitario: 15 }]
        }) });
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
  const browser = await chromium.launch({ headless: true,
    channel: process.env.F101_CHROME_CHANNEL || undefined,
    args: ['--no-sandbox'] });
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
    await page.goto(baseUrl + '/mis-ordenes');
    await page.getByRole('heading', { name: 'Acceso no autorizado' }).waitFor();

    // P3: validar que la UI alcanza el SKU 500 mediante búsqueda del servidor
    // y no calcula el total global a partir de la primera página.
    await page.goto(baseUrl + '/productos');
    await page.getByText('Página 1 de 200').waitFor({ timeout: 15000 });
    await page.locator('.table-footer button').last().click();
    await page.getByText('SKU-00051').waitFor({ timeout: 15000 });
    await page.getByPlaceholder('Buscar por SKU o nombre de producto...').fill('SKU-09999');
    await page.getByText('SKU-09999').waitFor({ timeout: 15000 });
    await page.locator('.table-footer').getByText('1 registro', { exact: true }).waitFor({ timeout: 15000 });
    console.log('P3_CATALOG_BROWSER_EVIDENCE=' + JSON.stringify({
      completed: true, totalSkus: 10000, pageSize: 50, laterPageLoaded: true,
      searchBeyondFirstPage: true, mockApi: true, productionTouched: false,
    }));

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
    await page.waitForURL('**/productos');
    assert.equal(await page.locator('.sidebar-nav').getByText('WMS / Bodega').count(), 0);
    assert.equal(await page.locator('.sidebar-nav').getByText('Órdenes').count(), 0);
    await page.getByRole('link', { name: 'Mis órdenes' }).click();
    await page.waitForURL('**/mis-ordenes');
    await page.getByRole('heading', { name: 'Mis órdenes' }).waitFor();
    await page.getByText('F104-OWN-A').first().waitFor();
    assert.equal(await page.getByText('F104-FOREIGN-B').count(), 0);
    await page.getByRole('button', { name: 'Ver detalle de F104-OWN-A' }).click();
    await page.getByText('SKU-OWN-A').waitFor();
    assert.equal(await page.getByText('DATOS_PRIVADOS_COMPRADOR_B').count(), 0);
    assert.ok(scenarios.requests.some(r => r.path === '/api/ordenes/mis-ordenes' && r.method === 'GET'));
    assert.equal(scenarios.requests.some(r => r.path === '/api/ordenes' && r.method === 'POST'), false);
    await page.goto(baseUrl + '/ordenes');
    await page.getByRole('heading', { name: 'Acceso no autorizado' }).waitFor();
    await page.goto(baseUrl + '/wms');
    await page.getByRole('heading', { name: 'Acceso no autorizado' }).waitFor();
    await page.getByRole('button', { name: 'Volver al inicio permitido' }).click();
    await page.waitForURL('**/productos');
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
