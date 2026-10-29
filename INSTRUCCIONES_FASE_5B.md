# LOGIMKT — Fase 5B
## WMS y transferencias reales entre ubicaciones

### Objetivo
Convertir la antigua reubicación de un saldo completo en una transferencia WMS real, parcial, auditable e idempotente sobre el modelo multiubicación de la Fase 4A.

### Cambios principales

- Nuevo `IInventoryService.TransferirStockAsync(...)`.
- La transferencia mueve únicamente `CantidadDisponible`.
- Unidades reservadas o bloqueadas permanecen en la ubicación de origen.
- Se valida capacidad de la ubicación destino.
- Se conserva el stock físico global del producto.
- Si el producto ya existe en destino, se incrementa su saldo; si no, se crea el saldo de esa ubicación.
- Se registran dos eventos en `MovimientoInventario`:
  - `TransferenciaSalida`
  - `TransferenciaEntrada`
- `OperacionId` hace la operación idempotente.
- Bloqueo de ubicaciones en orden estable para reducir carreras/deadlocks.
- Nuevo endpoint `GET /api/wms/stocks` para seleccionar saldos reales.
- Nuevo endpoint `POST /api/wms/transferir`.
- El endpoint antiguo `/api/wms/reubicar` se conserva como compatibilidad para mover un saldo completo sin reservas/bloqueos.
- La pantalla WMS ya no solicita escribir UUIDs manualmente: muestra SKU, ubicación y disponibilidad.

### No cambia

- No hay migración nueva.
- No ejecutar `dotnet ef database update`.
- No se modifica `CodigoEAN` ni ninguna funcionalidad de códigos de barras.
- No se modifica Finance, Checkout, RMA ni Fulfillment.

## Aplicación

1. Cerrar frontend/API si están en ejecución.
2. Extraer el ZIP de Fase 5B.
3. Copiar el contenido sobre la raíz de `ERP-Log-stica-` y aceptar reemplazos.
4. Ejecutar:

```powershell
cd frontend\marketplace-ui
npm.cmd run build
cd ..\..

dotnet build MarketplaceERP.sln
dotnet test MarketplaceERP.sln
```

### Resultado esperado

Se agregan 4 pruebas nuevas de Catalog respecto a Fase 5A:

- transferencia parcial conserva stock global;
- no mueve unidades reservadas/bloqueadas;
- reintento con mismo `OperacionId` no duplica stock;
- respeta capacidad de destino.

Si Fase 5A tenía 33 pruebas Catalog, el esperado es:

```text
Core:      6/6
Catalog:  37/37
Finance:  22/22
TOTAL:    65/65
```

Lo importante es `Failed: 0`.

## Commit sugerido

```powershell
git add .
git commit -m "Fase 5B: transferencias WMS reales entre ubicaciones"
git status
```

Debe terminar con:

```text
nothing to commit, working tree clean
```
