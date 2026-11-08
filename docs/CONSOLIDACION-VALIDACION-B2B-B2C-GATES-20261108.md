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

## 3A. Ensayos de recuperación entre Catalog y Finance completados en PostgreSQL aislado

Se añadió `tests/MarketplaceERP.Finance.Tests/FulfillmentOmsRecoveryPostgresTests.cs`, que usa únicamente `postgres@localhost:5432/postgres` del runner y crea/elimina una BD `ci_fulfillment_*` con migraciones reales de Catalog y Finance. Nunca se conecta a Neon.

| Caso probado | Fallo inyectado | Observación y recuperación |
|---|---|---|
| Generación PickList | PostgreSQL rechaza la transición OMS a `EnProceso` después del INSERT en WMS | **Defecto reproducido**: una segunda llamada creaba **dos PickLists para la misma orden**; CI [42528362653](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/42528362653). **Corregido**: el servicio consulta PickList existente, verifica orden/productos/SKU/cantidades y la reutiliza; si el contenido es incompatible, rechaza el reintento. Un `pg_advisory_xact_lock` de llave por `OrdenId` serializa sus peticiones simultáneas |
| Packing y guía | PostgreSQL rechaza guardar `GuiaEnvio` en Finance después de insertar el paquete WMS | Estado divergente reproducido: WMS conserva paquete, OMS no tiene guía; tras retirar el fallo, reintentar recupera la **misma guía**, sin un segundo paquete |
| Despacho | PostgreSQL rechaza mover OMS a `Enviada` **después** del consumo de stock y del cambio a `Despachado` en WMS | Estado divergente reproducido; el siguiente intento actualiza OMS sin nueva reducción del stock. Se verifica **un solo movimiento Outbound** y saldo correcto |
| Dos PickLists simultáneas | La prueba retiene externamente el candado transaccional por `OrdenId` y lanza dos operaciones | Ambos procesos esperan el bloqueo `pg_locks`, después reciben el **mismo ID** y solo se guarda una PickList; no cambia saldo físico |

**Evidencias CI:** tras corregir la duplicación, [42528604056](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/42528604056) pasó 505 pruebas .NET y E2E; [42528897454](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/42528897454) pasó 506 pruebas, E2E y PostgreSQL; [42529180587](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/42529180587) aprobó **507/507 pruebas .NET, cero fallos**, incluida simultaneidad. El trabajo E2E del último run estaba en ejecución durante la última lectura de esta anotación.

**Herramienta de diagnóstico:** `scripts/diagnosticar-divergencias-fulfillment.sql` es solo `SELECT` y busca listas duplicadas, PickLists sin avance OMS, guías desajustadas, paquetes duplicados, despachos sin estado OMS y paquetes huérfanos. **Aún no se ha ejecutado contra los datos del usuario y sus resultados requieren interpretación.**

**Guardia adicional de duplicados históricos:** `FulfillmentService.GenerarPickListAsync` comprueba hasta dos listas por `OrdenId` antes de reutilizar una. Si encuentra más de una, **detiene la operación** y exige conciliación manual sin borrar datos; también rechaza un reintento con SKU/producto/cantidad distintos. Dos pruebas `FulfillmentServiceTests.GenerarPickList_ConDuplicadosHistoricos_RechazaSinBorrarListas` y `GenerarPickList_RetryConCantidadDistinta_NoReutilizaUnaListaIncompatible` cubren ambos casos. CI [42529565282](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/42529565282): **509 pruebas .NET aprobadas** (Core 10, Catalog 79, Finance 420), cero fallos; frontend y PowerShell aprobados, E2E Browser en curso al documentarlo.

**Límites críticos:**

- El diseño **todavía NO tiene una transacción distribuida** entre Catalog y Finance. Se ha comprobado recuperación por reintento en circunstancias específicas, **NO recuperación automática** si el usuario nunca reintenta.
- El lock serializa llamadas que pasan por `FulfillmentService.GenerarPickListAsync`; no es un índice UNIQUE en la base ni protege escritores directos/legados. Añadir un índice único requiere antes inspeccionar duplicados históricos y migración no destructiva.
- No se han probado todas las fallas concurrentes ni las entregas en el mundo real. El test B2B/B2C integral actual es principalmente EF InMemory y necesita corroboración HTTP/API + PostgreSQL con datos sintéticos.
- Quedan pendientes los 23 archivos locales de recuperación, incluido el importador controlado y modificaciones de frontend.

## 3B. Recorrido HTTP/PostgreSQL de venta aprobada B2B y B2C — validado

**Nuevas pruebas**: `tests/MarketplaceERP.Finance.Tests/FlujoVentaB2B_B2CHttpPostgresTests.cs`, dos casos xUnit (`TipoCliente.B2C` y `TipoCliente.B2B`).

La prueba crea su propia base `ci_venta_integral_*` exclusivamente en `postgres@localhost:5432/postgres`, ejecuta las migraciones de Core, Catalog y Finance, y no conecta a Neon ni realiza pagos. Los tres contextos usan **la misma base física temporal**, con esquemas separados.

| Comprobación | Resultado |
|---|---|
| Core | Proveedor sintético activo persistido, consultado al finalizar |
| Catalog | Producto sintético, ubicación WMS, 12 unidades iniciales y reserva confirmada de 3 |
| Finance | Orden sintética `Aprobada` con tipo B2B/B2C, IVA del 13 %, comprobante correcto y partida de venta balanceada |
| Autenticación | `POST /api/fulfillment/ordenes/{id}/generar-picklist` sin JWT devuelve `401` |
| API real | `TestServer` ASP.NET + JWT Admin; HTTP para crear PickList, confirmar picking, empacar y despachar |
| OMS | Orden avanza a `Enviada`, conserva misma guía y registra `FechaEnvio` |
| WMS | Una PickList completada, un paquete despachado, 9 unidades restantes y cero reservadas |
| Recuperación | Doble llamada HTTP a despacho genera **un solo movimiento físico Outbound** |
| Finance | Partida de venta con `Debe=Haber`, detalle contabilizado y destino de cuenta correcto: Caja/Bancos para B2C y Cuentas por Cobrar para B2B |

Evidencia de código: commit `7c643e0ed8605bd9b12db595765ea9e0457a14f9`, GitHub Actions [42530293455](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/42530293455): **511/511 pruebas .NET, cero fallidas**, compilación frontend y prueba PowerShell aprobadas. La ejecución E2E general de Chromium continuaba en curso al redactar.

**Límites de la prueba, para no sobredimensionar la certificación:**

- El flujo arranca con una orden `Aprobada` construida por datos sintéticos: **no ejecuta CheckoutController, no autoriza ni cobra pagos, no prueba webhooks ni reembolsos**.
- La vinculación del proveedor Core con el producto Catalog se prueba como datos sintéticos coherentes, **no como integridad referencial cross-schema**.
- No equivale a un recorrido B2B comercial completo de cotización, crédito, 3-way match o liquidación, cubiertos parcialmente por otras pruebas.
- No prueba que el navegador frontend ejecute exactamente esos endpoints ni que los despliegues Render/Vercel/Neon tengan el mismo commit.
- La validación de RMA/recepción inversa, retiros reales, concurrencia de asientos y conciliación post-fallo entre todos los módulos sigue pendiente.

**Siguiente gate de cierre:** ejecutar checkout de ensayo sin pagos reales, recepción PO–factura–match por HTTP con roles y conciliación de asientos, devoluciones RMA con inventario y revisión de la rúbrica; comparar también los 23 archivos locales respaldados.

## 3C. Devolución RMA integrada y corrección de serialización JSON

Se amplió la prueba `FlujoVentaB2B_B2CHttpPostgresTests` (tanto B2C como B2B) para hacer por HTTP lo siguiente:

- `POST /api/rma` antes de que OMS esté `Entregada`: **400**, sin crear una devolución.
- Transición de OMS a `Entregada` **solo mediante datos sintéticos del test** (no se ejercita un endpoint real de transportista).
- Solicitud que supera la cantidad vendida: **400**.
- Solicitud de RMA de 1 unidad de una orden ya entregada: **201**.
- Inspección `ReStock` con `OperacionId` y ubicación WMS: **200**; devolución al saldo físico.
- Reintento de inspección con otro `OperacionId`: **200** y **un solo movimiento RmaRestock**.
- `GET /api/rma`: **200** con contrato JSON plano, sin referencias circulares a `Producto`/`Stock`.
- Conciliación final: de 12 unidades iniciales, 3 despachadas y 1 devuelta, stock físico **10**, sin reservas, con exactamente un movimiento Outbound y uno RmaRestock.

**Defecto reproducido y corregido:** la API RMA devolvía directamente la entidad `DevolucionRMA` cargada con `Producto`. Al inspeccionar, la serialización circular `Producto.Stock.Producto...` hacía fallar la respuesta HTTP. CI [42530614251](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/42530614251) reprodujo los dos fallos de B2B/B2C. `RmaController` devuelve ahora un `RmaResponse` plano con **los mismos campos requeridos por `frontend/marketplace-ui/src/types/index.ts`**. Se aplica a solicitar, inspeccionar y listar, sin modificar el esquema ni el seguimiento EF.

**Validación:** commit `09818e2f5a83c9a2309947dacedebedd7dcc7fe8`, CI [42530926686](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/42530926686): **511/511 pruebas .NET aprobadas**, cero fallos (Core 10 + Catalog 79 + Finance 422), frontend y PowerShell aprobados. La ejecución E2E general seguía en curso en la última verificación. Las pruebas cubren exclusivamente datos efímeros y no tienen acceso a Neon.

**Límites aún abiertos:** no se verificó pago ni reembolso; tampoco la entrega física real; la concurrencia de creación de dos RMA para el mismo producto/orden todavía necesita un test que garantice que no se exceda la cantidad vendida; y sigue faltando comparar los 23 archivos de los worktrees locales antes de cualquier fusión.

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
