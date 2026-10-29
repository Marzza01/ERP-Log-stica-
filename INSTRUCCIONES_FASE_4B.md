# LOGIMKT — Fase 4B: StockService real

## Objetivo

Reemplazar el servicio mock de stock por una implementación real sobre `CatalogDbContext` y PostgreSQL, conservando compatibilidad temporal con Checkout hasta Fase 4C.

## Cambios incluidos

- `StockService` real implementa `IStockService`.
- `CatalogServiceExtensions` registra `StockService` en lugar de `StockServiceMock`.
- Reserva stock real en `catalog.stock_fisico`.
- Respeta `CantidadBloqueada`.
- Distribuye una reserva entre múltiples ubicaciones.
- Persiste `ReservaStock` y `ReservaStockDetalle`.
- Registra movimientos `Reserva` y `LiberacionReserva`.
- Soporta reintento/idempotencia lógica para la misma orden/producto/cantidad.
- Usa `FOR UPDATE` sobre PostgreSQL para serializar reservas concurrentes del mismo producto.
- Liberar stock revierte únicamente `CantidadReservada`; nunca altera `CantidadTotal`.
- `ConfirmarDespachoAsync` conserva temporalmente el nombre legado, pero en 4B SOLO cambia la reserva a `Confirmada`. No consume físico.

## Importante

No hay cambios de esquema en Fase 4B. **No generar ni aplicar migraciones EF.**

No se modifica `CodigoEAN`.

## Aplicación

Copiar el contenido del ZIP sobre la raíz del repositorio, aceptando reemplazos.

## Validación

Desde la raíz:

```powershell
dotnet build MarketplaceERP.sln
dotnet test MarketplaceERP.sln
```

Resultado esperado si todos los tests nuevos pasan:

- Core: 6
- Catalog: 22
- Finance: 22
- Total: 50

## PostgreSQL local del usuario

Si Docker continúa publicado en 5433:

```powershell
$env:POSTGRES_HOST_PORT="5433"
$env:ConnectionStrings__DefaultConnection="Host=127.0.0.1;Port=5433;Database=marketplace_erp;Username=marketplace_user;Password=marketplace_pass;Include Error Detail=true"
```

Fase 4B no necesita `dotnet ef database update`.

## Qué todavía NO hace 4B

- No renombra `ConfirmarDespachoAsync`; eso ocurre en 4C.
- No consume stock físico al pagar.
- No conecta aún picking/packing/despacho real.
- No implementa Inbound.
- No implementa códigos de barras.
