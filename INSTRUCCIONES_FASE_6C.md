# LOGIMKT — Fase 6C: Alertas operativas de inventario

## Objetivo
Agregar alertas de inventario derivadas del stock real, con prioridad operativa, stock en tránsito y reconocimiento persistente sin modificar ni cerrar artificialmente la condición de inventario.

## Reglas implementadas
- **Sin existencia**: `Disponible <= 0` → prioridad **CRITICAL**.
- **Stock crítico**: `Disponible > 0 && Disponible <= StockMinimo` → prioridad **HIGH**.
- **Stock bajo**: `Disponible > StockMinimo && Disponible <= PuntoReorden` → prioridad **MEDIUM**.
- `Disponible = Físico - Reservado - Bloqueado`.
- Un producto inactivo/no publicado no genera alertas.
- Reconocer una alerta significa que fue revisada; **no cambia stock ni resuelve la condición**.
- La alerta desaparece automáticamente cuando la disponibilidad deja de estar en condición de alerta.
- Si la condición se resuelve y luego reaparece, vuelve como `OPEN`.
- Las PO `Enviada` y `RecibidaParcial` se consideran stock en tránsito para la recomendación, pero no eliminan la alerta hasta que exista recepción física.

## Punto de reorden en el modelo actual
El repositorio actual conserva `PuntoReorden` en cada `StockFisico`. Para un SKU multiubicación, 6C usa el mayor valor configurado entre sus saldos y nunca por debajo de `Producto.StockMinimo`.

Esto permite conservar las reglas validadas de alertas sin agregar otra columna a `Producto` en esta fase. `CodigoEAN` permanece sin cambios.

## Persistencia
Las alertas son derivadas y no se almacenan como filas permanentes. Solo se agrega:

`catalog.alertas_inventario_revision`

para persistir el reconocimiento (`ProductoId + Tipo`). La revisión se elimina cuando la condición cambia o se resuelve, de modo que una futura reactivación no quede reconocida por error.

## API
- `GET /api/alertas-inventario`
- `POST /api/alertas-inventario/{productoId}/reconocer`

## UI
Nueva ruta:

`/alertas-inventario`

Nueva opción **Alertas de inventario** dentro de Operaciones, con:
- resumen de alertas;
- prioridad;
- tipo;
- disponible/físico/reservado/bloqueado;
- mínimo y reorden;
- faltante a reorden;
- PO en tránsito;
- ubicación;
- recomendación;
- estado Pendiente/Revisada;
- filtros por tipo, prioridad y estado.

## Aplicación
Copiar el contenido del ZIP sobre la raíz de `ERP-Log-stica-` conservando la estructura de carpetas.

## Validación antes de migración
Desde la raíz:

```powershell
cd frontend\marketplace-ui
npm.cmd run build
cd ..\..

dotnet build MarketplaceERP.sln
dotnet test MarketplaceERP.sln
```

Partiendo del último resultado validado de 6B, se esperan aproximadamente:

- Core: 10/10
- Catalog: 50/50
- Finance: 22/22
- Total: 82/82
- Failed: 0

Se agregan 5 pruebas de Catalog:
1. sin stock → `OutOfStock/Critical`;
2. disponible en mínimo → `CriticalStock/High`;
3. entre mínimo y reorden → `LowStock/Medium`;
4. reconocer no modifica ni resuelve stock;
5. resolver y reactivar limpia el reconocimiento anterior.

## Migración
6C agrega `catalog.alertas_inventario_revision`, por lo que **sí requiere migración EF**.

Cuando build y tests pasen, generar solamente:

```powershell
dotnet ef migrations add Fase6CAlertasInventario `
  --project src/MarketplaceERP.Catalog `
  --startup-project src/MarketplaceERP.API `
  --context CatalogDbContext `
  --output-dir Migrations
```

**No ejecutar todavía `dotnet ef database update`.**

Compartir el `.cs`, `.Designer.cs` y `CatalogDbContextModelSnapshot.cs` para revisión antes de aplicar la migración.

## Fuera de alcance
- No se modifica `CodigoEAN` ni se implementan códigos de barras.
- No se crean órdenes de compra automáticamente.
- No se cambia stock al reconocer alertas.
- No se modifica la lógica financiera existente.
