# LOGIMKT — Resumen Fase 8A

La Fase 8A incorpora evidencia automatizada de que el núcleo del Grupo A funciona como un flujo integrado.

## Cobertura del ensayo

`Inbound → Inventario → Reserva → WMS → Bloqueo → Picking → Packing → Outbound → RMA → Desbloqueo → Conteo → Ajuste → Alertas → Ledger`

## Principales invariantes verificadas

- Reservar no descuenta stock físico.
- Transferir conserva el total global.
- Bloquear no cambia el físico.
- Despachar consume físico y reserva.
- RMA ReStock incrementa físico de forma controlada.
- Conteo genera ajuste, pero no altera stock por sí mismo.
- Solo aplicar un ajuste aprobado modifica el inventario.
- Reconocer una alerta no modifica stock.
- El ledger contiene los movimientos físicos esperados.

## Cambios de infraestructura

Ninguno. No hay migración nueva.

## Estado para cierre

Si el script `scripts/ValidarFase8GrupoA.ps1 -PostgresPort 5433` termina correctamente y el checklist manual no descubre incidencias, el proyecto puede pasar al cierre documental de Fase 8 y posteriormente al handoff de `CodigoEAN` de Fase 9.
