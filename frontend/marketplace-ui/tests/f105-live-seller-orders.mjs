/**
 * F105: navegador real + API Release .NET + PostgreSQL QA local.
 * Verifica aislamientos de dos vendedores, solo lecturas de Vendedor.
 * Sin pagos reales, ni Render/Neon/Vercel, ni cuentas ajenas.
 */
import assert from 'node:assert/strict';
import { chromium } from 'playwright';
import { spawn } from 'node:child_process';
import { randomUUID, randomBytes } from 'node:crypto';
import { setTimeout as delay } from 'node:timers/promises';

const origin = 'http://127.0.0.1:4176';
const backend = 'http://127.0.0.1:5000';
const adminEmail = process.env.ADMIN_EMAIL;
const adminPassword = process.env.ADMIN_PASSWORD;
assert.equal(process.env.CI, 'true', 'CI local required');
assert.ok(adminEmail?.endsWith('@erp.invalid') && adminPassword?.length >= 20,
  'Only temporary local Admin credentials accepted');

async function waitReady(url) {
  for (let tries=0; tries<100; tries++) {
    try { const res=await fetch(url); if (res.ok) return; } catch {}
    await delay(250);
  }
  throw new Error('Local service did not become ready: ' + url);
}

const vite=spawn(process.execPath,
  ['./node_modules/vite/bin/vite.js', '--host', '127.0.0.1', '--port','4176','--strictPort'],
  {stdio:'ignore', env:{...process.env, VITE_CHECKOUT_HABILITADO:'false'}});
let browser;
const checks=[];
const record=(key)=>{checks.push(key); console.log('F105_CHECK='+key+':PASS');};
const credentials=tag=>({
  email:'f105-'+tag+'-'+randomUUID()+'@erp.invalid',
  password:randomBytes(28).toString('base64url')
});
async function login(page, account, landing) {
  await page.goto(origin+'/login');
  await page.getByPlaceholder('nombre@ejemplo.com').fill(account.email);
  await page.locator('input[type=password]').fill(account.password);
  // The preceding CI E2E shares a real IP-based 5/min login limiter.
  // Respect HTTP 429 instead of disabling or bypassing the security policy.
  const submitAndObserve = async () => {
    const loginResponse = page.waitForResponse(res => {
      const u = new URL(res.url());
      return u.pathname === '/api/auth/login' && res.request().method() === 'POST';
    }, { timeout: 15000 });
    await page.getByRole('button',{name:'Ingresar al sistema'}).click();
    return loginResponse;
  };
  let response = await submitAndObserve();
  if (response.status() === 429) {
    console.log('F105_RATE_LIMIT=429_RECOGNIZED_WAIT_ONE_WINDOW');
    await delay(65000);
    response = await submitAndObserve();
  }
  assert.equal(response.status(), 200,
    'F105 login must receive 200 (or one bounded retry after expected 429)');
  await page.waitForURL('**'+landing, {timeout:18000});
  assert.ok(await page.evaluate(()=>localStorage.getItem('erp_token')));
}
async function logout(page){
  await page.getByRole('button',{name:'Cerrar sesión'}).click();
  await page.waitForURL('**/login');
  assert.equal(await page.evaluate(()=>localStorage.getItem('erp_token')),null);
}
async function sellerProbe(page, ownId, otherId) {
  return page.evaluate(async ({ownId, otherId}) => {
    const jwt=localStorage.getItem('erp_token');
    const headers={Authorization:'Bearer '+jwt};
    const get=async path=>{
      const res=await fetch(path,{headers});
      const raw=await res.text();
      return {status:res.status, body:raw};
    };
    const own=await get('/api/ordenes/mis-ordenes/'+ownId);
    const foreign=await get('/api/ordenes/mis-ordenes/'+otherId);
    const missing=await get('/api/ordenes/mis-ordenes/ffffffff-ffff-4fff-8fff-ffffffffffff');
    const page1=await get('/api/ordenes/mis-ordenes?pagina=1&tamano=1');
    const cancelled=await get('/api/ordenes/mis-ordenes?estado=Cancelada');
    const bad=await get('/api/ordenes/mis-ordenes?tamano=101');
    const global=await get('/api/ordenes');
    const kpis=await get('/api/ordenes/kpis');
    return {own, foreign, missing, page1, cancelled, bad, global, kpis};
  }, {ownId, otherId});
}
async function fixture(page, buyers) {
  return page.evaluate(async ({buyers})=>{
    const token=localStorage.getItem('erp_token');
    const headers={Authorization:'Bearer '+token,'Content-Type':'application/json'};
    async function call(path, body){
      const res=await fetch(path,{method:'POST',headers,body:JSON.stringify(body)});
      if(res.status!==201) throw new Error('F105 local fixture POST '+path+' status '+res.status);
      return res.json();
    }
    const sellerIds=[];
    for(let i=0;i<2;i++){
      const v=await call('/api/vendedores',{
        nombreEmpresa:'F105 empresa QA '+String(i+1),
        email:buyers[i].email,telefono:'0000-0000'
      });
      sellerIds.push(v.id);
      await call('/api/usuarios',{
        email:buyers[i].email,password:buyers[i].password,
        rol:'Vendedor',nombre:'Vendedor F105 '+String(i+1),
        vendedorId:v.id
      });
    }
    const buyer=buyers[2];
    await call('/api/usuarios',{
      email:buyer.email,password:buyer.password,
      rol:'Comprador',nombre:'Comprador F105'
    });
    const orders=[];
    for(let i=0;i<3;i++){
      const owner=i===2?1:0;
      const sku='F105-'+(owner?'B':'A')+'-'+(i+1);
      const made=await call('/api/ordenes',{
        compradorId:crypto.randomUUID(),
        vendedorId:sellerIds[owner],
        tipoCliente:0,
        tipoComprobante:'FacturaConsumidorFinal',
        condicionPago:'Inmediato',
        costoEnvio:0,
        direccionEntrega:'F105_DIRECCION_PRIVADA_'+(owner?'B':'A'),
        items:[{productoId:crypto.randomUUID(),sku,
          nombre:'Producto QA '+sku,cantidad:1,precioUnitario:(owner?900:10+i)}]
      });
      orders.push({id:made.id,numeroOrden:made.numeroOrden,sku,owner});
    }
    return {sellerIds,orders};
  }, {buyers});
}
try {
  await waitReady(backend+'/health/ready');
  await waitReady(origin+'/login');
  browser=await chromium.launch({headless:true,channel:process.env.F101_CHROME_CHANNEL||'chrome',
    args:['--no-sandbox']});
  const page=await browser.newPage();
  const errors=[];
  const requests=[];
  page.on('pageerror',e=>errors.push(e.message));
  page.on('request',req=>{
    const u=new URL(req.url());
    if(u.pathname.startsWith('/api/')){
      assert.equal(u.hostname,'127.0.0.1');
      requests.push({method:req.method(),path:u.pathname});
    }
  });
  const admin={email:adminEmail,password:adminPassword};
  const sellerA=credentials('seller-a'),sellerB=credentials('seller-b'),buyer=credentials('buyer');
  await login(page,admin,'/dashboard');
  record('ADMIN_LOGIN_REAL_JWT');
  const {sellerIds,orders}=await fixture(page,[sellerA,sellerB,buyer]);
  assert.equal(sellerIds.length,2);
  assert.equal(orders.length,3);
  assert.equal(orders.filter(o=>o.owner===0).length,2);
  const [a1,a2,b1]=orders;
  const global=await page.evaluate(async()=>{
    const token=localStorage.getItem('erp_token');
    const res=await fetch('/api/ordenes',{headers:{Authorization:'Bearer '+token}});
    return {status:res.status,body:await res.json()};
  });
  assert.equal(global.status,200);
  assert.equal(global.body.total,3);
  await page.goto(origin+'/mis-ordenes');
  await page.getByRole('heading',{name:'Acceso no autorizado'}).waitFor();
  record('ADMIN_RETAINS_GLOBAL_ORDERS_AND_CANNOT_USE_SELLER_ROUTE');

  await logout(page);
  const sellerWritesStart=requests.length;
  await login(page,sellerA,'/productos');
  await page.getByRole('link',{name:'Mis órdenes'}).click();
  await page.waitForURL('**/mis-ordenes');
  await page.getByRole('heading',{name:'Mis órdenes'}).waitFor();
  await page.getByText('2 órdenes encontradas').waitFor();
  await page.getByRole('button',{name:'Ver detalle de '+a1.numeroOrden}).waitFor();
  await page.getByRole('button',{name:'Ver detalle de '+a2.numeroOrden}).waitFor();
  assert.equal(await page.getByText(b1.numeroOrden).count(),0);
  await page.getByRole('button',{name:'Ver detalle de '+a1.numeroOrden}).click();
  await page.getByText(a1.sku).waitFor();
  assert.equal(await page.getByText(b1.sku).count(),0);
  record('SELLER_A_BROWSER_LIST_AND_DETAIL_ONLY_OWN');

  const probeA=await sellerProbe(page,a1.id,b1.id);
  assert.equal(probeA.own.status,200);
  assert.equal(probeA.foreign.status,404);
  assert.equal(probeA.missing.status,404);
  assert.equal(probeA.global.status,403);
  assert.equal(probeA.kpis.status,403);
  assert.equal(probeA.bad.status,400);
  assert.equal(probeA.page1.status,200);
  assert.equal(JSON.parse(probeA.page1.body).total,2);
  assert.equal(JSON.parse(probeA.page1.body).ordenes.length,1);
  assert.equal(JSON.parse(probeA.cancelled.body).total,0);
  assert.ok(!probeA.own.body.includes('F105_DIRECCION_PRIVADA'));
  assert.ok(!probeA.own.body.includes(b1.sku));
  record('SELLER_A_REAL_HTTP_JWT_403_404_PAGINATION_PRIVACY');

  await page.goto(origin+'/ordenes');
  await page.getByRole('heading',{name:'Acceso no autorizado'}).waitFor();
  await page.goto(origin+'/wms');
  await page.getByRole('heading',{name:'Acceso no autorizado'}).waitFor();
  await logout(page);
  const sellerARequests=requests.slice(sellerWritesStart)
    .filter(r=>r.method!=='GET' && r.path!=='/api/auth/login');
  assert.deepEqual(sellerARequests,[]);
  record('SELLER_A_NO_MUTATING_REQUESTS');

  const sellerBStart=requests.length;
  await login(page,sellerB,'/productos');
  await page.getByRole('link',{name:'Mis órdenes'}).click();
  await page.waitForURL('**/mis-ordenes');
  await page.getByText('1 orden encontrada').waitFor();
  await page.getByRole('button',{name:'Ver detalle de '+b1.numeroOrden}).waitFor();
  assert.equal(await page.getByText(a1.numeroOrden).count(),0);
  assert.equal(await page.getByText(a2.numeroOrden).count(),0);
  await page.getByRole('button',{name:'Ver detalle de '+b1.numeroOrden}).click();
  await page.getByText(b1.sku).waitFor();
  const probeB=await sellerProbe(page,b1.id,a1.id);
  assert.equal(probeB.own.status,200);
  assert.equal(probeB.foreign.status,404);
  assert.equal(probeB.missing.status,404);
  assert.equal(JSON.parse(probeB.page1.body).total,1);
  assert.equal(probeB.global.status,403);
  assert.equal(probeB.kpis.status,403);
  await logout(page);
  assert.deepEqual(requests.slice(sellerBStart)
    .filter(r=>r.method!=='GET' && r.path!=='/api/auth/login'),[]);
  record('SELLER_B_BROWSER_HTTP_ONLY_OWN_AND_READ_ONLY');

  await login(page,buyer,'/checkout');
  await page.goto(origin+'/mis-ordenes');
  await page.getByRole('heading',{name:'Acceso no autorizado'}).waitFor();
  const buyerStatus=await page.evaluate(async()=>{
    const token=localStorage.getItem('erp_token');
    return (await fetch('/api/ordenes/mis-ordenes',{headers:{Authorization:'Bearer '+token}})).status;
  });
  assert.equal(buyerStatus,403);
  await logout(page);
  record('BUYER_BLOCKED_FROM_SELLER_ORDERS');

  const anon=(await fetch(backend+'/api/ordenes/mis-ordenes')).status;
  assert.equal(anon,401);
  assert.deepEqual(errors,[]);
  record('ANONYMOUS_401_AND_NO_BROWSER_ERRORS');
  console.log('F105_REAL_E2E='+JSON.stringify({
    result:'PASS',checks:checks.length,
    browser:'Chrome',api:'Release-.NET-8',
    database:'ephemeral-local-PostgreSQL',
    sellers:2,ordersCreated:3,
    ownOrderCounts:[2,1],foreignDetails:404,
    sellerGlobalOrders:403,sellerGlobalKpis:403,
    buyerSellerRoute:403,anonymous:401,
    sellerMutations:0,externalPayments:0,externalDeployments:0,
    baselinePrSha:'aa94bd80e2a5f96070181d8a22727d046d18b230'
  }));
} finally {
  if(browser) await browser.close();
  vite.kill('SIGTERM');
}
