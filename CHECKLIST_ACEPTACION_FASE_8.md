# LOGIMKT — Checklist de aceptación integral del Grupo A

Usar este documento después de que la validación automatizada termine con `Failed: 0`.

## A. Inventario / WMS
- [ ] Los productos muestran stock real y no datos demo.
- [ ] `Disponible = Físico - Reservado - Bloqueado`.
- [ ] Una transferencia parcial conserva el total global del SKU.
- [ ] Stock reservado o bloqueado no puede transferirse como disponible.
- [ ] El mapa WMS refleja posiciones ocupadas correctamente.

## B. Inbound / Compras
- [ ] Una recepción se registra por ítem de PO y ubicación WMS.
- [ ] La recepción incrementa stock físico una sola vez ante reintentos.
- [ ] Se registra `MovimientoInventario.RecepcionInbound`.
- [ ] Una PO parcialmente recibida conserva el pendiente correcto.

## C. Reserva / Outbound / Fulfillment
- [ ] Reservar aumenta `CantidadReservada` y no reduce `CantidadTotal`.
- [ ] Confirmar reserva después del pago no consume físico.
- [ ] Picking usa las ubicaciones realmente reservadas.
- [ ] El despacho es el punto donde se reduce físico y reservado.
- [ ] Reintentar el despacho no descuenta dos veces.

## D. RMA
- [ ] `ReStock` exige ubicación WMS.
- [ ] Un RMA ReStock incrementa inventario una sola vez.
- [ ] `MermaScrap` y `Remate` no se mezclan automáticamente con stock normal.

## E. Ajustes / Conteos
- [ ] Crear/aprobar ajuste no cambia stock.
- [ ] Aplicar ajuste aprobado sí cambia stock y genera ledger.
- [ ] Conteo cíclico toma snapshot al iniciar.
- [ ] Una diferencia exige reconteo antes de generar ajuste.
- [ ] Si cambia el stock durante el conteo, el cierre se bloquea.

## F. Alertas / Reabastecimiento
- [ ] Las alertas se derivan del stock real.
- [ ] Reconocer alerta no modifica inventario.
- [ ] Una PO abierta aparece como cobertura/en tránsito, pero no elimina la alerta física.
- [ ] Reabastecimiento diferencia sin PO, cobertura parcial y cubierta por PO.

## G. Proveedores
- [ ] Compras selecciona proveedor real de `core.proveedores`.
- [ ] Proveedores no vinculados al catálogo se muestran como no vinculados; no se crean automáticamente.

## H. Frontend / UX
- [ ] Sin fallbacks demo en Productos, WMS, Fulfillment, RMA y Compras.
- [ ] Un fallo de API muestra error y no simula éxito.
- [ ] Build Vite termina sin errores TypeScript.
- [ ] No aparece warning de chunk > 500 kB.
- [ ] Paginación, modales y navegación móvil funcionan correctamente.

## I. Base de datos / arquitectura
- [ ] Catalog no tiene migraciones pendientes.
- [ ] `IStockService` está registrado con `StockService`, no `StockServiceMock`.
- [ ] PostgreSQL sigue siendo la fuente persistente del inventario operativo.
- [ ] `CodigoEAN` no fue alterado por el Grupo A.
