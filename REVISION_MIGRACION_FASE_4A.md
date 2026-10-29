# Revisión de migración Fase 4A

Migración revisada: `20261026174959_Fase4AInventarioMultiubicacion`.

## Confirmado como seguro

- No elimina ni recrea `catalog.productos`.
- No elimina `catalog.stock_fisico`.
- No modifica `Producto.CodigoEAN` ni su índice.
- El cambio `Cantidad` -> `CantidadOriginal` en reservas es un `RenameColumn`, por lo que conserva el valor.
- `CantidadBloqueada`, `CantidadLiberada` y `CantidadConsumida` nacen en 0.
- Amplía `Estado` de varchar(20) a varchar(30).
- Crea `movimientos_inventario` y `reservas_stock_detalle` como tablas nuevas.
- Cambia el índice único por Producto del stock a un índice no único + unicidad Producto/Ubicación.

## Correcciones añadidas antes de aplicar

1. Reserva histórica `Liberada`:
   - `CantidadLiberada = CantidadOriginal`.
   - establece `FechaFinalizacion`.

2. Reserva histórica `Despachada`:
   - se migra a `Consumida`.
   - `CantidadConsumida = CantidadOriginal`.
   - establece `FechaFinalizacion`.

3. Se crea automáticamente un `ReservaStockDetalle` para cada reserva histórica usando el único `StockFisico` que el modelo anterior permitía por producto.

4. La migración falla explícitamente si existe una reserva histórica cuyo producto no tiene `StockFisico`, evitando una migración parcial silenciosa.

5. El `Down()` mapea los nuevos estados a estados legados y se niega a volver al modelo 1:1 si ya existen varios saldos por producto.

## Riesgo que debe validarse contra la base antes de aplicar

Los nuevos CHECK de `stock_fisico` exigen:

- CantidadTotal >= 0
- CantidadReservada >= 0
- CantidadBloqueada >= 0
- CantidadReservada + CantidadBloqueada <= CantidadTotal

Como `CantidadBloqueada` nace en 0, el único riesgo histórico sería un registro donde `CantidadReservada > CantidadTotal`.
PostgreSQL rechazará la migración si existe uno, lo cual es preferible a introducir datos inválidos.

## No aplicar todavía

Antes de `dotnet ef database update` se debe resolver la conexión local a PostgreSQL y verificar qué migraciones están aplicadas.
