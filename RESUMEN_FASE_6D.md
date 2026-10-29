# Resumen — Fase 6D

Se añadió control operativo de stock bloqueado por daño, cuarentena o inspección.

- El stock físico no cambia.
- La disponibilidad sí cambia mediante `CantidadBloqueada`.
- Reservas quedan protegidas.
- Auditoría mediante `MovimientoInventario.BloqueoDano` y `MovimientoInventario.Desbloqueo`.
- Idempotencia por `OperacionId`.
- Nueva pantalla `/bloqueos-inventario`.
- 5 pruebas nuevas de Catalog.
- Sin migración de base de datos.
- `CodigoEAN` intacto.
