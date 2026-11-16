const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.join(__dirname, '..', 'src');
const app = fs.readFileSync(path.join(root, 'App.tsx'), 'utf8');
const sidebar = fs.readFileSync(path.join(root, 'components', 'layout', 'Sidebar.tsx'), 'utf8');

const routes = [...app.matchAll(/<Route path="([^"]+)" element=\{<RoleRouteGuard allowedRoles=\{\[([^\]]+)\]\}>/g)]
  .map(([, route, roles]) => ({
    route,
    roles: [...roles.matchAll(/'([^']+)'/g)].map((match) => match[1]),
  }));

test('El menú ofrece todas las pantallas navegables autorizadas al Admin', () => {
  const admin = routes.filter(({ route, roles }) => roles.includes('Admin') && !route.includes(':'));
  assert.ok(admin.length >= 19, 'Se espera el inventario completo de rutas administrativas');
  for (const { route } of admin) {
    assert.ok(sidebar.includes("to: '/" + route + "'"), 'Falta enlace administrativo: /' + route);
  }
});

test('Los flujos de comprador y vendedor conservan las fronteras de seguridad', () => {
  for (const [route, rol] of [['checkout', 'Comprador'], ['mis-ordenes', 'Vendedor']]) {
    const record = routes.find((r) => r.route === route);
    assert.deepEqual(record?.roles, [rol], route + ' no debe concederse a Admin sin soporte de API');
  }
});
