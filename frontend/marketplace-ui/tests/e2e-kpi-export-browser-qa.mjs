/**
 * E2E QA browser: descarga CSV real desde DashboardPage con API interceptada.
 * No se consumen credenciales verdaderas, no se envía ninguna petición fuera
 * de localhost, no se altera el dashboard productivo ni se toca una BD.
 */
import assert from 'node:assert/strict';
import { chromium } from 'playwright';
import { spawn } from 'node:child_process';
import { setTimeout as delay } from 'node:timers/promises';

const base = 'http://127.0.0.1:4178';
const vite = spawn(process.execPath, [
  './node_modules/vite/bin/vite.js', '--host', '127.0.0.1',
  '--port', '4178', '--strictPort',
], {
  stdio: ['ignore', 'pipe', 'pipe'],
  env: { ...process.env, VITE_CHECKOUT_HABILITADO: 'false' },
});
let consoleVite = '';
for (const pipe of [vite.stdout, vite.stderr]) {
  pipe.on('data', v => { consoleVite = (consoleVite + String(v)).slice(-1200); });
}

async function serverReady() {
  for (let i = 0; i < 80; i++) {
    if (vite.exitCode !== null) throw Error('Vite falló: ' + consoleVite);
    try {
      const response = await fetch(base + '/login');
      if (response.ok && (await response.text()).includes('LOGIMKT')) return;
    } catch { /* Inicio pendiente. */ }
    await delay(250);
  }
  throw Error('Vite no está disponible: ' + consoleVite);
}

function authToken() {
  const now = Date.now();
  const expiresAt = new Date(now + 10 * 60 * 1000).toISOString();
  const exp = Math.floor((now + 10 * 60 * 1000) / 1000);
  const data = Buffer.from(JSON.stringify({ role: 'Admin', exp })).toString('base64url');
  return { token: 'e30.' + data + '.qa-kpi-export', expiresAt,
    role: 'Admin', rol: 'Admin', usuario: 'Admin QA' };
}

function kpiPayload(days) {
  return {
    periodo: { desde: days === 7 ? '2026-11-02' : '2026-10-10',
      hasta: '2026-11-09', diasPeriodo: days },
    generadoEn: '2026-11-09T15:00:00Z',
    dbConectada: true,
    resumenEjecutivo: {
      totalOrdenes: 12, ordenesEntregadas: 9, ordenesCanceladas: 1,
      totalMatches3Way: 5, totalRmasPeriodo: 2, totalComisionGenerada: 85.50,
    },
    kpis: [
      { numero: 1, nombre: 'OTIF', valor: 0, unidad: '%', meta: 95,
        semaforo: 'Neutro', descripcion: 'Aún no medido', cumpleMeta: false,
        tieneDatos: false },
      { numero: 2, nombre: '=HYPERLINK("https://evil.invalid")', valor: 7.5,
        unidad: '%', meta: 5, semaforo: 'Amarillo',
        descripcion: 'Prueba seguridad', cumpleMeta: false, tieneDatos: true },
    ],
  };
}
const modelo = {
  generadoEn: '2026-11-09T15:00:00Z',
  supuestos: {},
  resumenAnual: { ventasTotalesAnuales: 0, comisionTotalAnual: 0,
    costosTotalesAnuales: 0, utilidadTotalAnual: 0 },
  meses: [],
};
const json = (status, body) => ({
  status, contentType: 'application/json', body: JSON.stringify(body),
});

async function downloadCsv(page) {
  const pending = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Exportar KPIs CSV' }).click();
  const downloaded = await pending;
  const stream = await downloaded.createReadStream();
  const parts = [];
  for await (const chunk of stream) parts.push(Buffer.from(chunk));
  return {
    name: downloaded.suggestedFilename(),
    csv: Buffer.concat(parts).toString('utf8'),
  };
}

async function main() {
  await serverReady();
  const browser = await chromium.launch({ channel: 'chrome', headless: true, args: ['--no-sandbox'] });
  const failures = [];
  try {
    const context = await browser.newContext({ acceptDownloads: true });
    const page = await context.newPage();
    page.on('pageerror', e => failures.push(e.message));

    let offline = false;
    const requests = [];
    await page.route(url => url.pathname.startsWith('/api/'), async route => {
      const req = route.request();
      const target = new URL(req.url());
      assert.equal(target.hostname, '127.0.0.1', 'Nunca llamar fuera de localhost');
      requests.push(target.pathname + target.search);
      if (target.pathname === '/api/auth/login') {
        const credentials = req.postDataJSON();
        assert.equal(credentials.email, 'qa-export@example.invalid');
        assert.equal(credentials.password, 'only-qa-local-password');
        return route.fulfill(json(200, authToken()));
      }
      if (target.pathname === '/api/kpis-dashboard/modelo-financiero')
        return route.fulfill(json(200, modelo));
      if (target.pathname === '/api/kpis-dashboard') {
        if (offline) return route.fulfill(json(503, {
          mensaje: 'Datos oficiales no disponibles. No fabricar KPIs.',
        }));
        return route.fulfill(json(200, kpiPayload(
          Number(target.searchParams.get('diasPeriodo') || 30))));
      }
      return route.fulfill(json(404, { mensaje: 'Ruta QA no implementada' }));
    });

    await page.goto(base + '/login');
    await page.getByPlaceholder('nombre@ejemplo.com')
      .fill('qa-export@example.invalid');
    await page.locator('input[type=password]').fill('only-qa-local-password');
    await page.getByRole('button', { name: 'Ingresar al sistema' }).click();
    await page.waitForURL('**/dashboard');
    await page.getByRole('heading', { name: 'Dashboard Ejecutivo' }).waitFor();
    const exportButton = page.getByRole('button', { name: 'Exportar KPIs CSV' });
    await exportButton.waitFor();
    await exportButton.waitFor({ state: 'visible' });
    await page.waitForFunction(() => {
      const b = [...document.querySelectorAll('button')]
        .find(x => x.textContent?.includes('Exportar KPIs CSV'));
      return b && !b.disabled;
    });

    const thirty = await downloadCsv(page);
    assert.equal(thirty.name, 'LOGIMKT-KPIs-30dias.csv');
    assert.ok(thirty.csv.startsWith('\uFEFF'));
    assert.ok(thirty.csv.includes('"OTIF","Sin datos"'));
    assert.ok(thirty.csv.includes("'=HYPERLINK"));
    assert.ok(!thirty.csv.includes('"=HYPERLINK'));
    assert.ok(thirty.csv.includes('"Órdenes registradas","12"'));
    assert.ok(thirty.csv.includes('"Desde","2026-10-10"'));
    console.log('OK KPI E2E: descarga CSV auténtica 30 días, sin falsos ceros ni fórmula CSV');

    await page.getByRole('button', { name: '7 días' }).click();
    await page.waitForFunction(() => {
      const b = [...document.querySelectorAll('button')]
        .find(x => x.textContent?.includes('Exportar KPIs CSV'));
      return b && !b.disabled;
    });
    const seven = await downloadCsv(page);
    assert.equal(seven.name, 'LOGIMKT-KPIs-7dias.csv');
    assert.ok(seven.csv.includes('"Días","7"'));
    assert.ok(seven.csv.includes('"Desde","2026-11-02"'));
    assert.ok(!seven.csv.includes('"Días","30"'));
    console.log('OK KPI E2E: cambio 30→7 días cambia el CSV exportado');

    offline = true;
    await page.getByRole('button', { name: 'Actualizar' }).click();
    await page.getByText('Error al cargar Dashboard').waitFor();
    assert.equal(await exportButton.isDisabled(), true);
    assert.equal(await page.locator('.stat-grid').count(), 0);
    console.log('OK KPI E2E: respuesta 503 bloquea exportar y no muestra datos sintéticos');

    assert.equal(failures.length, 0, 'Errores de navegador: ' + failures.join(' | '));
    assert.ok(requests.some(x => x.includes('diasPeriodo=30')));
    assert.ok(requests.some(x => x.includes('diasPeriodo=7')));
    console.log('KPI_EXPORT_BROWSER_QA_PASS_3_SCENARIOS_NO_EXTERNAL_API');
    await context.close();
  } finally {
    await browser.close();
  }
}
try {
  await main();
} finally {
  vite.kill('SIGTERM');
  await delay(200);
}
