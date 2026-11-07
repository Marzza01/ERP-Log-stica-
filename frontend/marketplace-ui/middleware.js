// Runs before the SPA rewrite. Vercel supplies VERCEL_ENV for each deployment.
export const config = { matcher: '/api/:path*' };

const productionOrigin = 'https://marketplaceerp-api.onrender.com';
const stagingOrigin = 'https://marketplaceerp-api-staging.onrender.com';
const hopHeaders = [
  'connection', 'content-length', 'host', 'keep-alive', 'proxy-authenticate',
  'proxy-authorization', 'te', 'trailer', 'transfer-encoding', 'upgrade',
];

function targetOrigin() {
  if (process.env.VERCEL_ENV === 'production') return productionOrigin;
  if (process.env.VERCEL_ENV === 'preview' &&
      process.env.API_STAGING_ORIGIN === stagingOrigin) return stagingOrigin;
  return null;
}

export default async function middleware(request) {
  const incoming = new URL(request.url);
  const origin = targetOrigin();
  if (!origin) return new Response('API de ensayo no configurada.', { status: 503,
    headers: { 'Cache-Control': 'no-store' } });
  if (!incoming.pathname.startsWith('/api/')) return new Response(null, { status: 404 });

  const upstreamPath = incoming.pathname === '/api/health' ? '/health'
    : incoming.pathname === '/api/health/ready' ? '/health/ready'
    : incoming.pathname;
  const upstream = new URL(upstreamPath + incoming.search, origin);
  const headers = new Headers(request.headers);
  for (const name of hopHeaders) headers.delete(name);
  // Current authentication is Bearer JWT. Do not forward Vercel's own cookies.
  headers.delete('cookie');
  for (const name of ['x-forwarded-for', 'x-forwarded-host', 'x-forwarded-proto']) headers.delete(name);

  try {
    const method = request.method.toUpperCase();
    const body = method === 'GET' || method === 'HEAD' ? undefined : await request.arrayBuffer();
    const response = await fetch(upstream, {
      method, headers, body, redirect: 'manual', signal: request.signal,
    });
    const responseHeaders = new Headers(response.headers);
    for (const name of hopHeaders) responseHeaders.delete(name);
    // fetch may decode an upstream body, so its original encoding/length cannot be reused.
    responseHeaders.delete('content-encoding');
    responseHeaders.delete('set-cookie');
    responseHeaders.set('Cache-Control', 'no-store');
    const noBody = method === 'HEAD' || [204, 205, 304].includes(response.status);
    return new Response(noBody ? null : response.body,
      { status: response.status, headers: responseHeaders });
  } catch {
    return new Response('No se pudo contactar la API.', { status: 502,
      headers: { 'Cache-Control': 'no-store' } });
  }
}
