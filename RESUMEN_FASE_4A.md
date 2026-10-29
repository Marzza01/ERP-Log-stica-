# LOGIMKT — Fase 4A / Modelo de Inventario

## Alcance

Este bloque modifica únicamente el modelo de dominio y EF Core del módulo Catalog para preparar el núcleo real de inventario.

### Incluye
- stock físico 1:N por producto/ubicación;
- `CantidadBloqueada`;
- disponibilidad `Total - Reservada - Bloqueada`;
- reserva con cantidades original/liberada/consumida;
- `ReservaStockDetalle` por saldo/ubicación;
- `MovimientoInventario` como ledger operativo;
- adaptación de Catálogo, RMA y consultas de Productos al nuevo modelo;
- tres pruebas adicionales de modelo.

### No incluye todavía
- `StockService` real;
- reserva transaccional;
- consumo por Fulfillment;
- Inbound real desde Compras;
- transferencia parcial;
- ajustes/conteos;
- cambios de códigos EAN.

## Migración

La migración EF Core no se incluye deliberadamente en el ZIP. Debe generarse en el equipo local después de que `dotnet build` y `dotnet test` sean correctos, para que EF Core genere automáticamente la migración y el snapshot a partir del estado exacto del repositorio integrado.

No ejecutar `dotnet ef database update` hasta revisar la migración generada.
