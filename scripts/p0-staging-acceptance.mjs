#!/usr/bin/env node
/**
 * Validación P0 de LOGIMKT contra el BACKEND STAGING, no producción.
 * No contiene credenciales. No ejecuta pagos, desembolsos ni asientos manuales.
 *
 * node scripts/p0-staging-acceptance.mjs --preflight
 * P0_EXECUTE_STAGING=CONFIRMO P0_ADMIN_EMAIL=... P0_ADMIN_PASSWORD=... \
 *   node scripts/p0-staging-acceptance.mjs --execute
 *
 * Alternativa: P0_ADMIN_TOKEN como variable del entorno local del operador.
 * No copiar tokens ni contraseñas en GitHub Actions o archivos del repositorio.
 */
import { randomUUID } from 'node:crypto';

const BASE = 'https://marketplaceerp-api-staging.onrender.com';
const args = process.argv.slice(2);
const mode = args[0];
const help = () => {
  console.log([
    'P0 LOGIMKT: prueba controlada exclusivamente en Render staging.',
    '  --preflight  GET /health, /health/ready y verificación de 401',
    '  --execute    Flujo sintético real, requiere P0_EXECUTE_STAGING=CONFIRMO',
    'Variables privadas (locales): P0_ADMIN_TOKEN o P0_ADMIN_EMAIL + P0_ADMIN_PASSWORD.',
    'No usar en producción. No ejecuta pagos; deja dos PO sintéticas para auditoría.'
  ].join('\n'));
};

if (mode === '--help' || mode === '-h' || !mode) {
  help();
  process.exit(mode ? 0 : 1);
}
if (!['--preflight', '--execute'].includes(mode)) {
  help();
  process.exit(2);
}

const response = async (method, path, token, body) => {
  const headers = { Accept: 'application/json' };
  if (token) headers.Authorization = `Bearer ${token}`;
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  const res = await fetch(BASE + path, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
    signal: AbortSignal.timeout(45000),
    redirect: 'manual',
  });
  const text = await res.text();
  let data;
  try { data = JSON.parse(text); } catch { data = text.slice(0, 250); }
  return { status: res.status, data };
};

const expected = (actual, status, title) => {
  if (actual.status !== status) {
    const details = typeof actual.data === 'string'
      ? actual.data
      : (actual.data?.mensaje ?? JSON.stringify(actual.data).slice(0, 240));
    throw new Error(`${title}: esperado HTTP ${status}, recibido ${actual.status}; ${details}`);
  }
  console.log(`OK ${title}: HTTP ${status}`);
  return actual.data;
};

const evidence = {
  timestamp: new Date().toISOString(),
  host: BASE,
  mode,
  createdPoIds: [],
  statuses: {},
  completed: false,
};

const fixture = Object.freeze({
  // Identificadores de demo de Neon staging; NO son proveedores contactados.
  proveedorId: 'd658cab6-1f5b-4aec-82ef-413e6e401d37',
  proveedorNombre: 'Importadora San Salvador S.A. de C.V.',
  ubicacionId: 'cfcba78f-9670-41aa-96ee-8c076850163c',
  sku1: 'ALM-0001',
  nombre1: 'Arroz Blanco Seleccionado Grano Largo 5 lb Standard',
  sku2: 'ALM-0005',
  nombre2: 'Café Gourmet Estricta Altura Molido 400g Standard',
});

const createPo = async (token, items, marker) => {
  const result = expected(await response('POST', '/api/compras/po', token, {
    proveedorId: fixture.proveedorId,
    proveedorNombre: fixture.proveedorNombre,
    observaciones: `P0-SMOKE-${marker}: datos sintéticos, no contactar proveedor`,
    terminosPago: 'Crédito 30 días',
    enviarInmediatamente: false,
    items,
  }), 201, 'crear PO de prueba');
  if (!result?.id) throw new Error('La respuesta de nueva PO no incluyó id');
  evidence.createdPoIds.push(result.id);
  console.log('PO de prueba creada:', result.id);
  return result.id;
};

const getPo = async (token, id) => expected(
  await response('GET', `/api/compras/po/${id}`, token),
  200, 'consultar PO');

const sendPo = async (token, id) => expected(
  await response('POST', `/api/compras/po/${id}/enviar`, token),
  200, 'enviar PO solo en registro interno');

const receive = (token, id, items, notes) => response(
  'PUT', `/api/compras/po/${id}/recibir`, token, { items, notas: notes });

const line = (po, sku) => {
  const item = po.items?.find(i => i.sku === sku);
  if (!item) throw new Error(`No se localizó SKU ${sku} en la PO`);
  return item.id;
};

const verify = (condition, message) => {
  if (!condition) throw new Error('Verificación incumplida: ' + message);
};

try {
  expected(await response('GET', '/health'), 200, 'API /health');
  expected(await response('GET', '/health/ready'), 200, 'bases /health/ready');
  expected(await response('GET', '/api/compras/po'), 401, 'Compras sin sesión');
  expected(await response('POST', '/api/liquidaciones/generar', null, {}),
    401, 'Liquidaciones sin sesión');

  if (mode === '--preflight') {
    evidence.completed = true;
  } else {
    if (process.env.P0_EXECUTE_STAGING !== 'CONFIRMO') {
      throw new Error('Las escrituras de prueba exigen P0_EXECUTE_STAGING=CONFIRMO');
    }
    let token = process.env.P0_ADMIN_TOKEN;
    if (!token) {
      const email = process.env.P0_ADMIN_EMAIL;
      const password = process.env.P0_ADMIN_PASSWORD;
      if (!email || !password)
        throw new Error('Necesita una cuenta Admin legítima en variables locales, nunca en el chat.');
      const login = expected(await response('POST', '/api/auth/login', null, {
        email, password,
      }), 200, 'inicio de sesión real');
      verify(login.role === 'Admin' || login.rol === 'Admin',
        'el usuario autenticado no tiene rol Admin');
      token = login.token;
    }
    verify(!!token, 'No se obtuvo JWT');
    expected(await response('GET', '/api/compras/po', token),
      200, 'Compras autenticado');

    // Comprobación obligatoria ANTES de escribir: en staging ninguna
    // operación financiera ni autorización de pago debe estar habilitada.
    expected(await response('POST', '/api/compras/match', token, {
      ordenCompraId: randomUUID(),
      facturaProveedorId: randomUUID(),
      cantidadRecibidaBodega: 1,
    }), 503, '3-Way financiero deshabilitado');
    expected(await response('POST', '/api/liquidaciones/generar', token, {
      vendedorId: randomUUID(),
    }), 503, 'generación de liquidaciones deshabilitada');
    expected(await response('POST',
      `/api/liquidaciones/procesar-pago/${randomUUID()}`, token),
      503, 'desembolsos deshabilitados');

    const marker = randomUUID().slice(0, 8);
    const poId = await createPo(token, [
      { sku: fixture.sku1, nombreProducto: fixture.nombre1,
        cantidad: 2, precioUnitario: 3.55 },
      { sku: fixture.sku2, nombreProducto: fixture.nombre2,
        cantidad: 3, precioUnitario: 10.06 },
    ], marker);
    await sendPo(token, poId);
    const po = await getPo(token, poId);
    const a = line(po, fixture.sku1), b = line(po, fixture.sku2);
    const operationA = randomUUID(), operationB = randomUUID();

    const inA = {
      itemOrdenCompraId: a, cantidadRecibida: 2,
      cantidadAcumuladaEsperada: 2,
      ubicacionAlmacenId: fixture.ubicacionId, operacionId: operationA,
    };
    const inB = {
      itemOrdenCompraId: b, cantidadRecibida: 3,
      cantidadAcumuladaEsperada: 3,
      ubicacionAlmacenId: fixture.ubicacionId, operacionId: operationB,
    };

    expected(await receive(token, poId, [
      inA, { ...inB, ubicacionAlmacenId: randomUUID() }
    ], 'P0 recepción parcial intencional'),
      409, 'fallo WMS en segunda línea conserva primera');
    let current = await getPo(token, poId);
    verify(current.items.find(i => i.id === a)?.cantidadRecibida === 2,
      'la primera línea debe persistir cantidad 2');
    verify(current.items.find(i => i.id === b)?.cantidadRecibida === 0,
      'la segunda línea no debe persistir cantidad');

    expected(await receive(token, poId, [inA, inB],
      'P0 reintento idempotente'), 200, 'recuperar recepción');
    current = await getPo(token, poId);
    verify(current.items.find(i => i.id === a)?.cantidadRecibida === 2,
      'cantidad arroz después de recuperación');
    verify(current.items.find(i => i.id === b)?.cantidadRecibida === 3,
      'cantidad café después de recuperación');

    expected(await receive(token, poId, [inA, inB], 'P0 replay exacto'),
      200, 'reintento exacto');
    expected(await receive(token, poId,
      [{ ...inA, operacionId: randomUUID() }], 'P0 token adulterado'),
      409, 'rechazo de token desconocido');

    const invoice = {
      ordenCompraId: poId,
      numeroFactura: `P0-${marker}`,
      subTotal: 37.28,
      montoTotal: 42.13,
      lineas: [
        { itemOrdenCompraId: a, cantidadFacturada: 2, precioUnitarioFacturado: 3.55 },
        { itemOrdenCompraId: b, cantidadFacturada: 3, precioUnitarioFacturado: 10.06 },
      ],
    };
    expected(await response('POST', '/api/compras/facturas', token, invoice),
      201, 'registrar factura de prueba');
    expected(await response('POST', '/api/compras/facturas', token, invoice),
      409, 'rechazo de factura duplicada');
    evidence.statuses.procurement = 'passed';

    // Dos solicitudes HTTP con distintos tokens contra la misma PO.
    const raceId = await createPo(token, [
      { sku: fixture.sku1, nombreProducto: fixture.nombre1,
        cantidad: 5, precioUnitario: 3.55 },
    ], marker + '-race');
    await sendPo(token, raceId);
    const race = await getPo(token, raceId);
    const raceLine = line(race, fixture.sku1);
    const raceBase = {
      itemOrdenCompraId: raceLine,
      cantidadRecibida: 5,
      cantidadAcumuladaEsperada: 5,
      ubicacionAlmacenId: fixture.ubicacionId,
    };
    const results = await Promise.all([
      receive(token, raceId, [{ ...raceBase, operacionId: randomUUID() }],
        'P0 concurrente A'),
      receive(token, raceId, [{ ...raceBase, operacionId: randomUUID() }],
        'P0 concurrente B'),
    ]);
    verify(results.filter(r => r.status === 200).length === 1,
      'exactamente una solicitud de recepción debe ser exitosa');
    verify(results.filter(r => r.status === 409).length === 1,
      'la segunda solicitud concurrente debe devolver 409');
    const raceFinal = await getPo(token, raceId);
    verify(raceFinal.items?.[0]?.cantidadRecibida === 5,
      'no duplicar las 5 unidades recibidas');
    evidence.statuses.concurrency = 'passed';
    evidence.statuses.paymentsGuard = 'disabled-503';
    evidence.completed = true;
  }
} catch (err) {
  evidence.error = err?.message ?? String(err);
  process.exitCode = 1;
} finally {
  // No incluir JWT, email ni contraseña en la evidencia.
  console.log('P0_EVIDENCE=' + JSON.stringify(evidence));
}
