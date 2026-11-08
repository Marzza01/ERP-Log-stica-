# Validación integral B2B/B2C — Matriz de ejecución segura

**Fecha:** 2026-11-08  
**Alcance:** análisis de los controladores y tests del repositorio en la rama `consolidacion/erp-segura-20261108`. Este documento es una **matriz de pruebas pendiente**, no un certificado de que todos los recorridos ya pasaron.

**Límites:** NO usar Neon producción, pagos reales ni desplegar; ejecutar contra PostgreSQL 15 desechable, gateway de pagos simulado, datos sintéticos y cuentas QA. No cambiar `main`, ni staging, ni fusionar PR #64.

## 1. Flujo B2B / aprovisionamiento con factura proveedor

| Paso | Punto de entrada observado | Validación requerida |
|---|---|---|
| Proveedor | `ProveedoresController`, en Core | Proveedor activo, identidad y saldos vinculados correctamente |
| PO | `ComprasController`: `POST /po`, `POST /po/{id}/enviar` | Un solo número PO, cantidades y precios correctos |
| Recepción | `ComprasController`: `PUT /po/{id}/recibir` | Recepción parcial / repetida / concurrente; movimiento de stock exacto; ningún ingreso duplicado |
| Factura proveedor | `ComprasController`: `POST /facturas`, `POST /facturas/{id}/lineas` | Importe/IVA/cantidades y referencias coherentes |
| 3-way match | `ComprasController`: `POST /match` con `PaymentOperationsGuard` | Coincidencia PO–recepción–factura, autorización única y rollback ante fallo contable |
| Contabilidad | `ContabilidadController`: libro diario, mayor y balance | Débitos = créditos, IVA y trazabilidad por orden única |
| Liquidación | `LiquidacionesController`: `POST /generar`, `POST /procesar-pago/{id}` con `PaymentOperationsGuard` | Idempotencia, pago único simulado y asientos balanceados |

**Gates negativos obligatorios:** misma PO recibida dos veces, recepción parcial seguida de otra recepción, dos matches concurrentes, factura con discrepancia, caída entre inventario y Finance, reintento de liquidación, roles Comprador/Vendedor sin permiso administrativo.

## 2. Flujo B2C / OMS – Catalog – WMS – Finance

| Paso | Punto de entrada observado | Validación requerida |
|---|---|---|
| Autenticación | JWT Comprador y `CheckoutController` | `401` sin sesión; comprador real GUID; Administración sin saltarse guardias |
| Compra | `CheckoutController`: `POST /api/checkout`, `Idempotency-Key` | Precio desde Catalog, un único cargo simulado y pedido por clave; mismo hash reintenta, distinto hash devuelve 409 |
| Stock | `CheckoutOrchestrator`, `StockService` | Reserva persistente exacta, no sobreventa ni cambios de stock hasta despacho |
| PickList | `FulfillmentController`: `POST /ordenes/{ordenId}/generar-picklist` | Orden aprobada, PickList única por orden y generación de ruta WMS |
| Picking | `POST /picklists/{pickListId}/confirmar` | No recoger cantidades excesivas, no repetir confirmación |
| Packing / guía | `POST /ordenes/{ordenId}/empacar` | Guía única, estado y transportista coincidentes entre WMS y OMS |
| Despacho | `POST /paquetes/{paqueteId}/despachar` | Reserva se consume una sola vez; orden Enviada y movimiento físico coincidentes |
| RMA | `RmaController`: `POST /api/rma`, `POST /api/rma/{id}/inspeccionar` | Devolver una sola vez, inspección y reintegro sin inflar stock |
| Finance | `ContabilidadController`, `LiquidacionesController` | Totales, IVA, asientos y split de vendedor reconciliados con la orden |
| Seguimiento | `CheckoutSeguimientoController` | Estado de orden coincide con paquete y pagos |

**Alcance actual del checkout:** `CheckoutController` acepta un producto por petición; el histórico multiproducto debe recuperarse únicamente tras pruebas de seguridad y atomicidad de todos los ítems.

## 3. Riesgo confirmado por lectura de código: transacciones entre módulos

`src/MarketplaceERP.API/Controllers/FulfillmentController.cs`:

- Líneas 61–70: `GenerarPickListAsync` en Catalog/WMS ocurre **antes** de `FinanceDbContext.SaveChangesAsync` que pasa OMS a `EnProceso`.
- Líneas 137–142: `EmpacarYRotularAsync` crea/prepara paquete en WMS **antes** de guardar `GuiaEnvio` y transportista en Finance.
- Líneas 168–177: `DespacharPaqueteAsync` efectúa la operación WMS **antes** de guardar el estado `Enviada` en Finance.

**Efecto potencial:** si falla la segunda escritura, la primera ya puede haber confirmado datos, dejando PickList, guía o despacho en Catalog sin el estado equivalente en Finance. **No consta aquí que el fallo se haya reproducido** ni que todas las implementaciones carezcan de recuperación, pero el orden de operaciones requiere un ensayo de caída entre escrituras.

**Gate antes de corregir:** ejecutar fallo de Finance inmediatamente después de cada escritura en Catalog usando PostgreSQL desechable y comprobar reintento idempotente o conciliación por `OrdenId`/`PaqueteId`. No añadir una transacción SQL compartida a ciegas entre distintos DbContext/esquemas.

## 4. Cobertura que existe y lo que todavía NO demuestra

- `tests/MarketplaceERP.Finance.Tests/FlujoIntegralE2ETests.cs` encadena PO–3Way–venta–Picking–Packing–Despacho–Liquidación y verifica balance, **pero utiliza EF Core InMemory para Core, Catalog y Finance**; no demuestra restricciones de PostgreSQL, integración HTTP/JWT ni consistencia bajo fallos entre DbContext.
- `tests/MarketplaceERP.Catalog.Tests/GrupoALogisticaE2ETests.cs` prueba Inbound–reservas–transferencia–bloqueo–Fulfillment–RMA–conteo cíclico–alertas, también con **EF Core InMemory**.
- Los tests de navegador y PostgreSQL de GitHub Actions son adicionales; que CI pase no sustituye un único recorrido empresarial completo B2B/B2C a través de la API real.
- `tests/MarketplaceERP.Catalog.Tests/CatalogImportAtomicPostgresTests.cs` prueba importaciones CSV atomicidad/reintentos y concurrencia con base temporal; no prueba todo el flujo empresarial.

## 5. Orden de ejecución recomendado para aceptación (antes de 2026-11-09)

1. Evidencia read-only de autenticación y endpoints en la instancia QA disponible, nunca usar producción ni pagos reales.
2. Postgres desechable: registrar PO, recibir cantidades parciales, facturar, ejecutar match una sola vez y reconciliar inventario/contabilidad.
3. Comprador sintético: checkout de un producto, reintento de idempotencia, picking, packing, despacho y RMA, con conciliación Catalog–Finance.
4. Inyección de fallo entre operaciones de Catalog y Finance en PickList, packing y despacho; verificar capacidad de reparación/reintento seguro antes de aplicar cambios.
5. Contrastar requisitos de rúbrica y diferencias locales de los 23 archivos respaldados, una vez facilitado `ERP_Revision_23_Archivos_20261108_143610.zip`; no sustituir código local ni limpiar ramas sin aprobación.
6. Emitir matriz de evidencia con hash de commit, resultados, fecha, contexto, SKUs sintéticos, ordenes, saldos y asientos; preparar versión candidata **solo cuando todos los P0 estén verdes**.

**Estado del trabajo:** diagnóstico y gates documentados. No se ejecutaron compras, despachos, devoluciones ni pagos reales en producción.
