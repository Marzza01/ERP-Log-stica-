# Resumen — Fase 6C: Alertas operativas de inventario

6C incorpora alertas derivadas del inventario real y conserva la regla de que reconocer una alerta no equivale a resolverla.

## Tipos y prioridades
- `OUT_OF_STOCK` → `CRITICAL`
- `CRITICAL_STOCK` → `HIGH`
- `LOW_STOCK` → `MEDIUM`

## Disponibilidad
`CantidadDisponible = CantidadFisica - CantidadReservada - CantidadBloqueada`.

## Reorden
Se reutiliza el `PuntoReorden` existente de `StockFisico`; en multiubicación se toma el máximo, nunca menor que `Producto.StockMinimo`.

## Stock en tránsito
PO `Enviada`/`RecibidaParcial` enriquecen la recomendación y muestran unidades pendientes, pero la alerta sigue activa hasta que el stock sea físicamente recibido.

## Reconocimiento
Solo se persiste `AlertaInventarioRevision`. Si la condición cambia o se resuelve, el reconocimiento deja de aplicar. Una reactivación futura nace nuevamente como `OPEN`.

## Persistencia nueva
`catalog.alertas_inventario_revision`.

La migración debe generarse en el equipo del usuario y revisarse antes de `database update`.
