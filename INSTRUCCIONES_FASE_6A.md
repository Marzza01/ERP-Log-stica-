# LOGIMKT — Fase 6A: Ajustes de inventario

## Objetivo

Agregar correcciones auditables de stock con un flujo controlado:

`Crear → Aprobar/Rechazar → Aplicar`

Crear o aprobar una solicitud **no modifica** el inventario. El stock cambia únicamente al aplicar un ajuste aprobado.

## Funciones incluidas

- Ajuste positivo y negativo por saldo/ubicación.
- Validación de disponibilidad para ajustes negativos.
- Estados: PendienteAprobacion, Aprobado, Rechazado, Aplicado y Cancelado.
- Ledger `MovimientoInventario` con `AjustePositivo` / `AjusteNegativo`.
- Reintento idempotente al aplicar.
- Pantalla `/ajustes` en Operaciones.
- API `/api/inventario/ajustes`.
- 4 pruebas nuevas de Catalog.

## Importante

Esta fase **sí modifica el modelo EF Core** al agregar `catalog.ajustes_inventario`.

Primero aplique el ZIP y ejecute:

```powershell
cd frontend\marketplace-ui
npm.cmd run build
cd ..\..

dotnet build MarketplaceERP.sln
dotnet test MarketplaceERP.sln
```

No ejecute todavía `database update`.

Si build/tests pasan, genere la migración:

```powershell
dotnet ef migrations add Fase6AAjustesInventario --project src/MarketplaceERP.Catalog --startup-project src/MarketplaceERP.API --context CatalogDbContext --output-dir Migrations
```

Luego revise/comparta la migración antes de aplicarla.

## Reglas

- Ajuste positivo: incrementa `CantidadTotal` al aplicar.
- Ajuste negativo: reduce solo stock disponible, nunca reservado o bloqueado.
- Un ajuste no aprobado no puede aplicarse.
- Un ajuste aplicado no vuelve a modificar inventario si se reintenta.
- `CodigoEAN` no se modifica.
