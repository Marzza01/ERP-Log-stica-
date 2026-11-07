/**
 * P1: browser acceptance of the actual protected Vercel staging Preview and
 * Render staging API with locally supplied Admin credentials. No data writes.
 *
 * Run only on the operator's trusted computer. Credentials and short-lived
 * Vercel OIDC token are read from environment, never logged or stored.
 *
 * Unlike the CI browser tests, this script validates the REAL staging stack.
 */
import assert from 'node:assert/strict';
import { chromium } from 'playwright';

const target = process.env.P1_STAGING_PREVIEW_URL;
const email = process.env.P1_ADMIN_EMAIL;
const password = process.env.P1_ADMIN_PASSWORD;
const oidc = process.env.VERCEL_OIDC_TOKEN;
let origin;

function validateTarget() {
  if (!target) throw Error('Falta P1_STAGING_PREVIEW_URL de Vercel Preview.');
  const url = new URL(target);
  if (url.protocol !== 'https:' ||
      !/^erp-log-stica-[a-z0-9]+-2-cm2\.vercel\.app$/.test(url.hostname) ||
      url.username || url.password || url.search || url.hash || url.pathname !== '/')
    throw Error('Destino no autorizado: se requiere URL exacta de Vercel Preview staging.');
  origin = url.origin;
  if (email !== 'admin.evaluacion@erp.example' || !password || password.length < 10)
    throw Error('Usar únicamente credenciales locales de Admin staging.');
  if (!oidc) throw Error('VERCEL_OIDC_TOKEN ausente: usar el acceso protegido de Vercel CLI.');
}
validateTarget();

const statuses = {};
let browser;
let countBrowserErrors = 0;
let phase = 'init';

async function start() {
  const launchOptions = { headless: true };
  if (process.env.P1_CHROME_CHANNEL === 'chrome') launchOptions.channel = 'chrome';
  browser = await chromium.launch(launchOptions);
  const context = await browser.newContext({
    viewport: { width: 1365, height: 850 },
    extraHTTPHeaders: { 'x-vercel-trusted-oidc-idp-token': oidc }
  });
  const page = await context.newPage();
  page.on('pageerror', () => { countBrowserErrors += 1; });

  phase = 'preview-login';
  const open = await page.goto(origin + '/login', { waitUntil: 'domcontentloaded', timeout: 40000 });
  assert.ok(open?.ok(), 'Vercel Preview no respondió 200; verificar Deployment Protection y Trusted Sources.');
  await page.getByRole('heading', { name: 'Iniciar sesión' }).waitFor({ timeout: 20000 });
  console.log('OK navegador: login de Vercel Preview protegido visible');

  phase = 'anonymous';
  const anon = await page.evaluate(async () => ({
    health: (await fetch('/api/health', { redirect: 'manual' })).status,
    ready: (await fetch('/api/health/ready', { redirect: 'manual' })).status,
    compras: (await fetch('/api/compras/po', { redirect: 'manual' })).status
  }));
  statuses.health = anon.health;
  statuses.ready = anon.ready;
  statuses.anonCompras = anon.compras;
  assert.equal(anon.health, 200);
  assert.equal(anon.ready, 200);
  assert.equal(anon.compras, 401);
  console.log('OK navegador: salud/readiness 200, compras sin login 401');

  phase = 'admin-login';
  await page.getByPlaceholder('nombre@ejemplo.com').fill(email);
  await page.locator('input[type=password]').fill(password);
  await page.getByRole('button', { name: 'Ingresar al sistema' }).click();
  await page.waitForURL('**/dashboard', { timeout: 20000, waitUntil: 'domcontentloaded' });
  await page.getByRole('heading', { name: 'Dashboard Ejecutivo' }).waitFor({ timeout: 20000 });
  const account = await page.evaluate(() => {
    const user = JSON.parse(localStorage.getItem('erp_usuario') || '{}');
    return { role: user.rol, tokenPresent: !!localStorage.getItem('erp_token') };
  });
  assert.equal(account.role, 'Admin');
  assert.equal(account.tokenPresent, true);
  console.log('OK navegador: login real Admin en Vercel Preview + Render staging');

  phase = 'read-only-get';
  const dataStatuses = await page.evaluate(async () => {
    const jwt = localStorage.getItem('erp_token');
    if (!jwt) throw Error('Sin token de sesión.');
    const paths = [
      '/api/compras/po',
      '/api/productos?pagina=1&tamano=1',
      '/api/ordenes?pagina=1&tamano=1'
    ];
    const results = [];
    for (const path of paths) {
      const res = await fetch(path, {
        method: 'GET', redirect: 'manual',
        headers: { Authorization: 'Bearer ' + jwt }
      });
      results.push(res.status);
    }
    return results;
  });
  statuses.comprasAdmin = dataStatuses[0];
  statuses.productosAdmin = dataStatuses[1];
  statuses.ordenesAdmin = dataStatuses[2];
  assert.deepEqual(dataStatuses, [200, 200, 200]);
  console.log('OK navegador: lectura real Admin de compras, catálogo y órdenes');

  phase = 'refresh';
  await page.reload({ waitUntil: 'domcontentloaded' });
  await page.waitForURL('**/dashboard', { timeout: 20000 });
  assert.equal(await page.evaluate(() => !!localStorage.getItem('erp_token')), true);
  console.log('OK navegador: sesión legítima persiste tras recarga');

  phase = 'role-guard';
  await page.goto(origin + '/checkout', { waitUntil: 'domcontentloaded' });
  await page.getByRole('heading', { name: 'Acceso no autorizado' }).waitFor({ timeout: 20000 });
  console.log('OK navegador: Admin no adquiere acceso de Comprador');

  phase = 'logout';
  await page.getByRole('button', { name: 'Cerrar sesión' }).click();
  await page.waitForURL('**/login', { timeout: 20000 });
  await page.getByRole('heading', { name: 'Iniciar sesión' }).waitFor();
  assert.equal(await page.evaluate(() => localStorage.getItem('erp_token')), null);
  await page.reload({ waitUntil: 'domcontentloaded' });
  await page.getByRole('heading', { name: 'Iniciar sesión' }).waitFor();
  assert.equal(await page.evaluate(() => localStorage.getItem('erp_token')), null);
  console.log('OK navegador: logout real y recarga sin sesión');

  assert.equal(countBrowserErrors, 0, 'Errores JavaScript del navegador durante la aceptación.');
  console.log('P1_PREVIEW_EVIDENCE=' + JSON.stringify({
    timestamp: new Date().toISOString(),
    environment: 'vercel-preview-render-neon-staging',
    previewHost: new URL(origin).hostname,
    mode: 'real-chromium-authenticated-readonly',
    statuses, roles: ['Admin'], completed: true,
    operationsWithWrites: 0, payments: 'not-executed'
  }));
}

try {
  await start();
} catch (error) {
  const safeName = error instanceof Error ? error.name : 'UnknownError';
  console.error('P1_PREVIEW_EVIDENCE=' + JSON.stringify({
    timestamp: new Date().toISOString(),
    environment: 'vercel-preview-render-neon-staging',
    mode: 'real-chromium-authenticated-readonly',
    phase, statuses, browserErrors: countBrowserErrors,
    completed: false, errorType: safeName
  }));
  process.exitCode = 1;
} finally {
  if (browser) await browser.close();
}
