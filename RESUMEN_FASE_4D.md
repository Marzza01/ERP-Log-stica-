# Resumen — Fase 4D Fulfillment / Outbound

Estado preparado para validación local.

## Resultado funcional esperado

Antes del despacho:
- físico: 10
- reservado: 3
- disponible: 7
- reserva: Confirmada

Después del despacho:
- físico: 7
- reservado: 0
- disponible: 7
- reserva: Consumida
- paquete: Despachado
- movimiento: DespachoOutbound = 3

Segundo intento de despacho:
- no vuelve a disminuir físico;
- no crea un segundo consumo;
- devuelve el mismo paquete despachado.

## Alcance

Incluido:
- picking desde ReservaStockDetalle;
- consumo Outbound;
- idempotencia;
- EstadoPacking.Despachado;
- rechazo de despacho inválido;
- frontend sin falso éxito local en despacho;
- E2E actualizado al ciclo reserva → picking → packing → despacho.

Pendiente:
- Fase 4E Inbound / recepción real;
- ajustes;
- conteos;
- transferencias;
- códigos de barras.
