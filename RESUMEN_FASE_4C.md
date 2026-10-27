# Resumen Fase 4C

El Checkout queda semánticamente separado del despacho físico.

Flujo final de esta fase:

```text
Reservar stock
→ Procesar pago
   ├─ falla: liberar reserva
   └─ éxito: confirmar reserva
              ↓
          físico sin cambios
```

El consumo del stock físico se implementará en la fase de Fulfillment/Outbound.

`CodigoEAN` permanece sin cambios y pendiente para otro integrante.
