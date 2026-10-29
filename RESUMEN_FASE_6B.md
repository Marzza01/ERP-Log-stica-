# Resumen — Fase 6B: Conteos cíclicos

La fase incorpora conteos cíclicos persistentes para cada saldo `StockFisico`.

## Estados
- `Planificado`
- `EnConteo`
- `RequiereReconteo`
- `Completado`
- `CompletadoConAjuste`
- `Cancelado`

## Regla principal
El conteo físico nunca actualiza inventario directamente. Una diferencia confirmada por reconteo genera un `AjusteInventario` pendiente de aprobación. Solo la aplicación posterior de ese ajuste modifica `StockFisico` y crea el movimiento correspondiente.

## API
- `GET /api/inventario/conteos-ciclicos`
- `POST /api/inventario/conteos-ciclicos`
- `POST /api/inventario/conteos-ciclicos/{id}/iniciar`
- `POST /api/inventario/conteos-ciclicos/{id}/primer-conteo`
- `POST /api/inventario/conteos-ciclicos/{id}/reconteo`
- `POST /api/inventario/conteos-ciclicos/{id}/cancelar`

## UI
Nueva ruta `/conteos` y opción **Conteos cíclicos** en Operaciones.

## Persistencia
Nueva entidad `ConteoCiclico` / tabla prevista `catalog.conteos_ciclicos`. La migración debe generarse y revisarse en el equipo del usuario antes de `database update`.
