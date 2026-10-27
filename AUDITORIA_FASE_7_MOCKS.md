# Auditoría Fase 7 — Mocks, fallbacks y datos simulados

## Eliminados en 7A
- Auth frontend: token/admin automático y login fallback.
- Productos: catálogo demo + CRUD local optimista ante fallo.
- WMS: ubicaciones demo.
- Fulfillment: PickLists/paquetes demo, picking/packing local ante fallo.
- RMA: devolución demo + UUID precargados + KPI ficticio.
- Compras: PO/matches demo, factura local y 3-Way Match local.

## Pendientes para 7B / coordinación
### Compartidos de UI
- `Topbar.tsx`: `DEMO_NOTIFICATIONS`.
- `DashboardPage.tsx`: KPIs y modelo financiero demo en error de API.

### Fuera del núcleo del Grupo A
- `ContabilidadPage.tsx`: partidas/balance demo.
- `LiquidacionesPage.tsx`: liquidaciones demo/locales.
- `OrdenesPage.tsx`: órdenes demo y actualizaciones locales.
- `CommissionServiceMock`: servicio Core activo, responsabilidad compartida/Equipo 1.
- `PagoServiceMock`: pasarela Finance activa, responsabilidad compartida/Equipo 3.

### Legado no productivo
- `StockServiceMock`: conservado para pruebas aisladas; no es la implementación registrada de inventario real.

### Autenticación backend
- `AuthController` todavía mantiene usuarios de desarrollo en memoria. 7A elimina el bypass del frontend, pero no rediseña el módulo de identidad compartido.

## Criterio usado
Un fallo de API nunca debe transformarse en un éxito visual ni en registros inventados. Las pantallas del Grupo A ahora vacían/retienen el estado real y exponen el error para reintento.
