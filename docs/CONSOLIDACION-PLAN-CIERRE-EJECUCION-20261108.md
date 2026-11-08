# Plan de cierre controlado — ejecución 2026-11-08

**Rama de trabajo:** `consolidacion/erp-segura-20261108`. **Base:** `2d1c37748fe645d75c16168dd1d3b6214285bcea`. **PR:** #64 (draft).

> No declarar el ERP certificado ni fusionar a main solo por CI verde. No activar pasarela de pagos, desplegar producción ni borrar ramas antiguas durante esta fase.

## Avance por etapa

| Etapa | Evidencia ya ejecutada | Pendiente obligatorio |
|---|---|---|
| 1. Matriz 34 ramas | `docs/CONSOLIDACION-MATRIZ-34-RAMAS-20261108.md` | Auditoría semántica final de archivos exclusivos y aceptación de descartes |
| 2. Aislamiento/rescate | Rama de consolidación y 4 referencias Git de recuperación creadas | Backup independiente y restauración probada de producción Neon; revisar secretos sin exponerlos |
| 3. Riesgos P0 | Guards OMS y orden URL/cuerpo, regresiones .NET; WMS completado inmutable y reempaque bloqueado | Concurrencia real y restricciones DB; RMA, contabilidad, outbox/reconciliador |
| 4. Historial útil | Identificadas mejoras: carrito multiproducto, secuencias logísticas y manejo de concurrencia | Recuperar mediante PR independientes preservando seguridad y migraciones actuales |
| 5. E2E/seguridad/restauración | 474 tests CI base; pruebas nuevas y CI PR #64 | 5 B2B + 5 B2C completos; 50 pedidos concurrentes; falla inyectada entre DbContexts; restore verificado |
| 6. Rúbrica | Comparación documental en auditorías previas | 5 proveedores, 10 pedidos, carrito, 3 métodos simulados, 8 KPI correctos, ABC, forecast 20 productos, evidencias |
| 7. Main | PR #60 sigue borrador; respaldo Git de main | Activar protección/ruleset, revisar triggers Render/Vercel y respaldos; aprobación explícita antes de promoción |
| 8. Limpieza | Clasificación de 88 ramas, 34 divergentes catalogadas | Tras relevo de despliegues, archivar/etiquetar, retirar solo ramas confirmadas redundantes |

## P0 corregido en esta rama

1. `FulfillmentController.EmpacarYRotular` rechaza `OrdenId` distinto entre ruta y cuerpo y `NumeroOrden` diferente a la orden de Finance; comprueba estado OMS `EnProceso` antes de mutar WMS.
2. `FulfillmentController.GenerarPickList` exige estado `Aprobada` y usa `SkuReferencia` real, no un SKU inventado a partir del GUID.
3. `FulfillmentService.ConfirmarPickingAsync` rechaza modificar cantidades de una lista ya completada.
4. `FulfillmentService.EmpacarYRotularAsync` no crea otro paquete cuando ya existe uno despachado para esa orden.

**Alcance limitado:** no resuelve doble solicitud simultánea, inconsistencias Catalog/Finance en fallo parcial ni despacho directo de órdenes terminales. No establecer restricciones UNIQUE(OrdenId) sin decidir antes si se permiten múltiples bultos físicos. Las regresiones de punto 3/4 son de EF InMemory y no certifican concurrencia PostgreSQL.

## Pruebas y puertas obligatorias restantes

- [ ] CI completa de último SHA del PR #64 (3/3 jobs).
- [ ] Test PostgreSQL de dos PickLists simultáneas de una misma orden.
- [ ] Test de dos guías y reempaque después de despachar.
- [ ] Test de orden cancelada y entrega ya confirmada que no retrocede de estado por rutas alternativas.
- [ ] Test `ordenId` de URL distinto del cuerpo; comprobar cero escrituras en ambos esquemas con PostgreSQL.
- [ ] Test RMA asociado a venta real y saldo comprado/restante.
- [ ] Test asientos únicos de venta con aprobaciones concurrentes y conciliación de IVA, comisión, costo de envío y pasivo vendedor.
- [ ] Test checkout y despacho con fallos inyectados entre persistencias Finance/Catalog, outbox o compensación.
- [ ] Pruebas HTTP reales en entorno Development aislado contra PostgreSQL desechable; sin dinero real.
- [ ] Dos restauraciones probadas: esquema/datos de producción en infraestructura independiente y snapshot staging para demo.
- [ ] Activar protecciones de `main` antes de cambios y revisar despliegue automático Vercel Production / Render rama de integración.
- [ ] Aprobar explícitamente la promoción y después el lote de limpieza.

## Referencias de recuperación

- `archive/preconsolidacion-main-20261108` → `e04c90696e59d86390216a8b51bf00e876cfebc5`.
- `archive/preconsolidacion-staging-20261108` → `2d1c37748fe645d75c16168dd1d3b6214285bcea`.
- `archive/preconsolidacion-render-prod-20261108` → `48e63d987da96dac64f60afafbd9905dc8b222bd`.
- `archive/preconsolidacion-render-staging-20261108` → `d9e907c0ca7538350cc71d881c2e0f2b12be3153`.

**No se han realizado cambios de producción, datos de Neon ni fusiones a staging/main.**
