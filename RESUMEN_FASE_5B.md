# Resumen — Fase 5B WMS / Transferencias

La Fase 5B reemplaza la reubicación conceptual del WMS por una transferencia real entre saldos de `StockFisico`.

## Regla

```text
Disponible = Total - Reservado - Bloqueado
```

Solo `Disponible` puede transferirse.

## Flujo

```text
Stock origen
  ↓ validar disponible
Ubicación destino
  ↓ validar capacidad
Transferencia
  ├─ origen CantidadTotal ↓
  ├─ destino CantidadTotal ↑
  ├─ stock global = sin cambios
  ├─ MovimientoInventario.TransferenciaSalida
  └─ MovimientoInventario.TransferenciaEntrada
```

## Seguridad

- idempotencia por `OperacionId`;
- bloqueo de ubicaciones en orden estable;
- validación de capacidad;
- reserva y stock bloqueado no se mueven;
- endpoint legado conservado solo para compatibilidad.

## UI

La pantalla WMS permite seleccionar el saldo real mediante SKU + ubicación + disponible, elegir cantidad y destino, sin ingresar UUIDs manualmente.

## Base de datos

No requiere migración.

## Código de barras

`CodigoEAN` permanece sin modificaciones y pendiente para otro integrante.
