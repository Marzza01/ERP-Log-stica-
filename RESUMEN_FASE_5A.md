# Resumen — Fase 5A

## RMA → Inventario

Flujo objetivo implementado:

```text
Solicitud RMA
   ↓
Inspección técnica
   ↓
Clasificación
   ├─ ReStock
   │    ↓
   │  Seleccionar ubicación WMS
   │    ↓
   │  Validar capacidad
   │    ↓
   │  Aumentar StockFisico de esa ubicación
   │    ↓
   │  MovimientoInventario = RmaRestock
   │    ↓
   │  RMA = Completado
   │
   ├─ MermaScrap → no regresa a stock disponible
   └─ Remate     → no se mezcla con stock normal en 5A
```

### Garantías
- Idempotencia fuerte por `RmaId`.
- Reintentos no duplican stock.
- Ubicación WMS obligatoria para ReStock.
- Capacidad WMS validada.
- RMA no se completa si el movimiento de inventario falla.
- No hay cambios de esquema ni migraciones.
- `CodigoEAN` permanece sin modificaciones.
