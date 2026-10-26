# LOGIMKT — Fase 4A: Modelo de Inventario

Este bloque parte de la Fase 3 validada (frontend build OK + 41/41 tests).

## Qué cambia
- `Producto` pasa de un único `Stock` persistido a `Stocks` (1:N).
- Se mantiene `Producto.Stock` como compatibilidad temporal no persistida para no romper el contrato visual actual.
- `StockFisico` agrega `CantidadBloqueada`.
- Disponibilidad: `Total - Reservada - Bloqueada`.
- `ReservaStock` separa original/liberada/consumida.
- Se agrega `ReservaStockDetalle` para asignación por ubicación.
- Se agrega `MovimientoInventario` como ledger operativo.
- Catálogo, RMA y endpoints de Productos se adaptan al modelo multiubicación.
- Se agregan pruebas de bloqueado, multiubicación y reserva mixta finalizada.

## Importante
Este ZIP NO incluye una migración EF generada a mano. Después de compilar y probar, la migración debe generarse con `dotnet ef` en la máquina de desarrollo para que EF Core actualice también el snapshot de forma exacta.

NO ejecutar `database update` todavía. Primero generar la migración y revisar su contenido.

## Código de barras
No se modifica `CodigoEAN`. Sigue reservado para otro integrante.
