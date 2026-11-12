/** Verificación temporal del enlace docente; GET solamente, sin contraseñas. */
import assert from 'node:assert/strict';
import { chromium } from 'playwright';

const url = process.env.EVAL_SHARE_URL;
if (!url) throw new Error('EVAL_SHARE_URL requerida');
const target = new URL(url);
if (target.protocol !== 'https:' ||
    target.hostname !== 'erp-log-stica-git-staging-catalog-500-neon-test-2-cm2.vercel.app' ||
    !target.searchParams.has('_vercel_share')) {
  throw new Error('Destino de evaluación no autorizado');
}
let browser;
let pageErrors = [];
try {
  browser = await chromium.launch({ headless: true, channel: 'chrome' });
  const ctx = await browser.newContext({
    viewport: { width: 1366, height: 850 },
    ignoreHTTPSErrors: false
  });
  const page = await ctx.newPage();
  page.on('pageerror', error => { pageErrors.push(error.message); });

  const start = await page.goto(url, {
    waitUntil: 'domcontentloaded', timeout: 45000
  });
  assert.equal(start?.status(), 200);
  assert.equal(new URL(page.url()).hostname, target.hostname);
  console.log('PASS: página inicial HTTP 200 sin Vercel SSO');

  const login = await page.goto(target.origin + '/login', {
    waitUntil: 'domcontentloaded', timeout: 35000
  });
  assert.equal(login?.status(), 200);
  await page.getByRole('heading', { name: /Iniciar sesión/i }).waitFor({ timeout: 18000 });
  console.log('PASS: formulario de login visible');

  const res = await page.evaluate(async () => {
    const result = {};
    for (const path of ['/api/health', '/api/health/ready',
                       '/api/productos?pagina=1&tamano=1', '/api/compras/po']) {
      const r = await fetch(path, { credentials: 'same-origin', redirect: 'manual' });
      result[path] = r.status;
      if (path === '/api/health' || path === '/api/health/ready') {
        result[path + ':body'] = (await r.text()).slice(0, 200);
      }
    }
    return result;
  });
  assert.equal(res['/api/health'], 200);
  assert.equal(res['/api/health/ready'], 200);
  assert.equal(res['/api/productos?pagina=1&tamano=1'], 401);
  assert.equal(res['/api/compras/po'], 401);
  console.log('PASS: API salud/readiness 200 y autorización anónima 401');
  console.log('HEALTH_BODY=' + res['/api/health:body']);
  console.log('READY_BODY=' + res['/api/health/ready:body']);

  await page.goto(target.origin + '/dashboard', {
    waitUntil: 'domcontentloaded', timeout: 30000
  });
  await page.waitForURL(/\/login(?:[?#]|$)/, { timeout: 20000 });
  console.log('PASS: dashboard sin sesión redirige a login');
  assert.deepEqual(pageErrors, []);
  console.log('PASS: 0 errores JavaScript del navegador');
  console.log('EVALUACION_STAGING_GUEST_SMOKE_OK');
} finally {
  if (browser) await browser.close();
}
