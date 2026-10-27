# LOGIMKT — Fase 6D: Bloqueo y desbloqueo de inventario

## Objetivo
Gestionar unidades físicamente presentes pero temporalmente no vendibles por daño, cuarentena, inspección u otra incidencia operativa.

## Regla de negocio
- `CantidadTotal` NO cambia al bloquear ni desbloquear.
- Bloquear aumenta `CantidadBloqueada` y reduce `CantidadDisponible`.
- Desbloquear reduce `CantidadBloqueada` y aumenta `CantidadDisponible`.
- Las unidades reservadas nunca pueden bloquearse.
- Cada operación queda auditada en `catalog.movimientos_inventario`.
- Tipos usados: `BloqueoDano` y `Desbloqueo`.
- Las operaciones son idempotentes por `OperacionId`.

## Cambios incluidos
### Backend
- `IInventoryService`: nuevas operaciones `BloquearStockAsync` y `DesbloquearStockAsync`.
- `InventoryService`: validación, bloqueo de fila PostgreSQL, idempotencia y movimientos de ledger.
- Nuevo controlador `BloqueosInventarioController`:
  - `POST /api/inventario/bloqueos/bloquear`
  - `POST /api/inventario/bloqueos/desbloquear`

### Frontend
- Nueva ruta `/bloqueos-inventario`.
- Nueva pantalla "Bloqueos de inventario".
- Resumen de físico, bloqueado y disponible.
- Acciones por saldo WMS para bloquear/desbloquear con motivo obligatorio.

### Pruebas
Se agregan 5 pruebas de Catalog:
1. Bloqueo reduce disponible sin alterar físico.
2. Bloqueo respeta unidades reservadas y ya bloqueadas.
3. Reintento de bloqueo no duplica unidades.
4. Desbloqueo restaura disponibilidad sin alterar físico.
5. No permite desbloquear más unidades de las bloqueadas.

## Base de datos
**No requiere migración.** `CantidadBloqueada` y los tipos de movimiento ya existen desde Fase 4A.

No ejecutar `dotnet ef migrations add` ni `dotnet ef database update` para esta fase.

## Aplicación
Copiar el contenido del ZIP sobre la raíz de `ERP-Log-stica-`, conservando la estructura de carpetas.

## Validación
```powershell
cd frontend\marketplace-ui
npm.cmd run build
cd ..\..

dotnet build MarketplaceERP.sln
dotnet test MarketplaceERP.sln
```

Si 6C quedó con 50 pruebas de Catalog, 6D debería llevar Catalog aproximadamente a 55 pruebas. Lo importante es `Failed: 0`.

## Commit sugerido
```powershell
git add .
git commit -m "Fase 6D: bloqueo y desbloqueo operativo de inventario"
git status
```

## Fuera de alcance
- Conversión de stock dañado a merma definitiva: debe hacerse mediante el flujo de Ajustes cuando corresponda.
- `CodigoEAN` / códigos de barras: permanece sin modificaciones y pendiente para el otro integrante.
