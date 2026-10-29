# LOGIMKT — Fase 5C: Reabastecimiento operativo

## Objetivo

Incorporar una vista operativa de reabastecimiento basada en el inventario real y en las órdenes de compra abiertas, sin duplicar responsabilidades de Compras/Finanzas.

## Qué cambia

- Nuevo `IReabastecimientoService` / `ReabastecimientoService` en Catalog.
- Nuevo endpoint `GET /api/reabastecimiento/necesidades`.
- Nueva pantalla `/reabastecimiento` en el frontend.
- Nuevo acceso en el menú de Operaciones.
- La necesidad usa disponibilidad real:
  - físico
  - reservado
  - bloqueado
  - disponible
- Se consideran PO abiertas (`Borrador`, `Enviada`, `RecibidaParcial`) para no sugerir acciones duplicadas.
- No se crea una PO automáticamente: la responsabilidad formal permanece en Compras.

## Reglas principales

Un producto aparece cuando:

`CantidadDisponible <= StockMinimo`

Criticidad:

- `AGOTADO`: disponible <= 0
- `BAJO_MINIMO`: disponible < mínimo
- `EN_PUNTO_REORDEN`: disponible == mínimo

La API calcula además:

- unidades pendientes en compras abiertas;
- disponibilidad proyectada;
- faltante proyectado para salir de la alerta;
- estado de cobertura: `SIN_PO`, `COBERTURA_PARCIAL`, `CUBIERTA_POR_PO`.

## Aplicación

Copiar el contenido del ZIP sobre la raíz de `ERP-Log-stica-` y aceptar reemplazos.

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

Se agregan 3 pruebas nuevas a `MarketplaceERP.Catalog.Tests`.

## Base de datos

No hay cambios de esquema.

- NO ejecutar `dotnet ef migrations add`.
- NO ejecutar `dotnet ef database update`.

## Después de validar

```powershell
git add .
git commit -m "Fase 5C: reabastecimiento operativo con cobertura de compras"
git status
```

## Fuera de alcance

- Código de barras / EAN.
- Creación automática de PO.
- Selección formal del proveedor maestro (Fase 5D).
- Cambios financieros o contables.
