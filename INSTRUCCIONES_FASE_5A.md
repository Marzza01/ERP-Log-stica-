# LOGIMKT — Fase 5A: RMA → Inventario

## Objetivo
Integrar el dictamen `ReStock` del módulo RMA con el núcleo real de Inventario.

## Cambios principales
- `RmaService` ya no incrementa directamente `StockFisico`.
- `ReStock` exige una ubicación WMS de destino.
- `InventoryService.RecibirRmaRestockAsync` incrementa el saldo de esa ubicación.
- Se registra `MovimientoInventario` de tipo `RmaRestock` con `RmaId`.
- Un mismo RMA no puede incrementar inventario dos veces, incluso si el cliente genera un nuevo `OperacionId` al reintentar.
- Se valida capacidad de la ubicación antes del reingreso.
- Si el reingreso falla, el RMA no queda marcado como completado.
- `MermaScrap` y `Remate` no se mezclan con stock disponible normal en este bloque.
- La UI de RMA permite elegir ubicación WMS cuando el dictamen es `ReStock`.

## Aplicación
Copiar el contenido del ZIP sobre la raíz del repositorio `ERP-Log-stica-` y aceptar reemplazos.

## Validación
Frontend:

```powershell
cd frontend\marketplace-ui
npm.cmd run build
cd ..\..
```

Backend:

```powershell
dotnet build MarketplaceERP.sln
dotnet test MarketplaceERP.sln
```

No ejecutar migraciones: 5A no cambia el esquema.

## Pruebas nuevas
Se agregan pruebas para:
1. ReStock en ubicación seleccionada + movimiento de inventario.
2. Reintento idempotente sin doble incremento.
3. ReStock sin ubicación rechazado.
4. ReStock que excede capacidad rechazado sin completar RMA.

## Git
Si todo pasa:

```powershell
git add .
git commit -m "Fase 5A: integrar RMA ReStock con inventario"
git status
```

## Fuera de alcance
- Código de barras / EAN: pendiente para otro integrante.
- Transferencias WMS: Fase 5B.
- Reabastecimiento: bloque posterior de Fase 5.
- Proveedores logísticos: bloque posterior de Fase 5.
