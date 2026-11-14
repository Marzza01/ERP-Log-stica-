import assert from 'node:assert/strict';
import { pathToFileURL } from 'node:url';
import path from 'node:path';
const moduleUrl=pathToFileURL(path.resolve(import.meta.dirname,'../middleware.js')).href;
const {default: middleware}=await import(moduleUrl);
const originalFetch=globalThis.fetch;
const originalV=process.env.VERCEL_ENV;
const originalA=process.env.API_STAGING_ORIGIN;
const observed=[];
try {
 globalThis.fetch=async (url,options)=>{ observed.push(String(url));return new Response('OK',{status:200,headers:{'content-type':'text/plain'}}); };
 const ask=async (requestPath)=>middleware(new Request('https://preview.example.test'+requestPath));
 process.env.VERCEL_ENV='preview';delete process.env.API_STAGING_ORIGIN;
 let r=await ask('/api/health');
 assert.equal(r.status,503);assert.match(await r.text(),/API de ensayo no configurada/);assert.equal(observed.length,0);
 console.log('OK 1: Preview sin API_STAGING_ORIGIN falla cerrado HTTP 503, 0 red');
 process.env.API_STAGING_ORIGIN='https://marketplaceerp-api-staging.onrender.com';
 r=await ask('/api/health/ready');
 assert.equal(r.status,200);assert.equal(observed.at(-1),'https://marketplaceerp-api-staging.onrender.com/health/ready');
 console.log('OK 2: Preview con origen exacto enruta health/ready a Render staging');
 process.env.API_STAGING_ORIGIN='https://otro-host.example.test';observed.length=0;
 r=await ask('/api/health');assert.equal(r.status,503);assert.equal(observed.length,0);
 console.log('OK 3: Preview con origen no autorizado responde 503');
 process.env.VERCEL_ENV='production';delete process.env.API_STAGING_ORIGIN;
 r=await ask('/api/health');assert.equal(r.status,200);assert.equal(observed.at(-1),'https://marketplaceerp-api.onrender.com/health');
 console.log('OK 4: Produccion usa servicio Render API de su propia rama heredada');
 console.log('CHECKS_PASSED=4');
}finally{
 globalThis.fetch=originalFetch;
 if(originalV===undefined)delete process.env.VERCEL_ENV;else process.env.VERCEL_ENV=originalV;
 if(originalA===undefined)delete process.env.API_STAGING_ORIGIN;else process.env.API_STAGING_ORIGIN=originalA;
}
