# Fase 4C — Checkout y ciclo correcto de reserva

## Objetivo
Eliminar la ambigüedad entre confirmar una reserva después del pago y realizar un despacho físico.

## Cambios
- `IStockService.ConfirmarDespachoAsync` pasa a llamarse `ConfirmarReservaAsync`.
- `StockService` confirma la reserva sin modificar `CantidadTotal` ni `CantidadReservada`.
- `StockServiceMock` conserva el mismo contrato para pruebas/compatibilidad.
- `CheckoutOrchestrator` llama a `ConfirmarReservaAsync` después de pago exitoso.
- Pago fallido sigue compensando mediante `LiberarStockAsync`.
- Se actualizan pruebas para verificar que Checkout confirma la reserva y no la libera en el flujo exitoso.

## No cambia
- Esquema de PostgreSQL.
- Migraciones.
- Fulfillment.
- Descuento de stock físico.
- Código de barras/EAN.

## Validación
Desde la raíz:
```powershell
dotnet build MarketplaceERP.sln
dotnet test MarketplaceERP.sln
```

Resultado esperado: 50/50 pruebas o más si el repositorio incorpora nuevas pruebas.

No ejecutar `dotnet ef database update`: Fase 4C no modifica el modelo de datos.
