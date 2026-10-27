# LOGIMKT — Fase 6B: Conteos cíclicos

## Objetivo
Agregar conteos cíclicos auditables sobre saldos `StockFisico` sin modificar inventario directamente.

Flujo implementado:

1. **Planificar** un conteo para un saldo/ubicación.
2. **Iniciar** el conteo: se captura un snapshot de `CantidadTotal`.
3. **Primer conteo**:
   - Si coincide con el snapshot, el conteo se completa sin ajuste.
   - Si difiere, pasa obligatoriamente a `RequiereReconteo`.
4. **Reconteo**:
   - Si coincide con el sistema, se completa sin ajuste.
   - Si confirma la diferencia, se crea un `AjusteInventario` en `PendienteAprobacion`.
5. El stock solo cambia posteriormente mediante el flujo normal de **Ajustes de inventario**: aprobar → aplicar.

## Protecciones
- Un conteo no modifica `StockFisico`.
- No puede existir más de un conteo activo para el mismo saldo.
- No se permite planificar si existe un ajuste pendiente/aprobado sobre el saldo.
- Si `CantidadTotal` cambia después de iniciar el conteo, el registro se rechaza para evitar ajustes contra un snapshot obsoleto.
- El reconteo es idempotente: un reintento no crea un segundo ajuste.
- Cantidades contadas deben ser enteros >= 0.
- `CodigoEAN` no fue modificado.

## Aplicación
Copiar el contenido del ZIP sobre la raíz de `ERP-Log-stica-`, conservando la estructura de carpetas.

## Validación antes de migración
Desde la raíz del repositorio:

```powershell
cd frontend\marketplace-ui
npm.cmd run build
cd ..\..

dotnet build MarketplaceERP.sln
dotnet test MarketplaceERP.sln
```

Partiendo del último resultado validado de 6A, se esperan aproximadamente:

- Core: 10/10
- Catalog: 45/45
- Finance: 22/22
- Total: 77/77
- Failed: 0

## Migración
Esta fase agrega una nueva tabla `catalog.conteos_ciclicos`, por lo que **sí requiere migración EF**.

Cuando build y tests estén correctos, generar solamente la migración:

```powershell
dotnet ef migrations add Fase6BConteosCiclicos `
  --project src/MarketplaceERP.Catalog `
  --startup-project src/MarketplaceERP.API `
  --context CatalogDbContext `
  --output-dir Migrations
```

**No ejecutar todavía `dotnet ef database update`.**

Compartir la migración generada (`.cs`, `.Designer.cs` y `CatalogDbContextModelSnapshot.cs`) para revisión antes de aplicarla.

## No incluido
- No se modifica `CodigoEAN` ni funciones de códigos de barras.
- No se implementan todavía alertas operativas (Fase 6C).
- No se altera la lógica financiera.
