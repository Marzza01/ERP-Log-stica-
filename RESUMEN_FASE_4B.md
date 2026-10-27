# Resumen — Fase 4B StockService real

Fase 4B sustituye la reserva simulada en memoria por persistencia real en PostgreSQL.

Reglas implementadas:

```text
Reservar
→ CantidadReservada aumenta
→ CantidadTotal NO cambia
→ Disponible disminuye

Confirmar después del pago
→ Estado = Confirmada
→ físico NO cambia
→ reservado NO cambia

Liberar
→ CantidadReservada disminuye
→ CantidadTotal NO cambia
→ Disponible vuelve a aumentar
```

Disponibilidad:

```text
CantidadDisponible = CantidadTotal - CantidadReservada - CantidadBloqueada
```

Una reserva puede asignarse a varios `StockFisico`/ubicaciones mediante `ReservaStockDetalle`.

La protección de concurrencia en PostgreSQL bloquea las filas de stock del producto con `FOR UPDATE` antes de comprobar y asignar disponibilidad.

`StockServiceMock` puede permanecer en el código como referencia temporal, pero deja de estar registrado en DI y ya no se usa en ejecución normal.
