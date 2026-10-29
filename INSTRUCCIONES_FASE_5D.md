# LOGIMKT — Fase 5D: Proveedores logísticos

## Objetivo

Consumir el maestro único `core.proveedores` desde Grupo A sin crear una tabla paralela ni duplicar responsabilidades.

## Cambios principales

- Nuevo endpoint `GET /api/proveedores/logistica`.
- Nueva vista **Proveedores logísticos** en Operaciones.
- Reabastecimiento vincula `Producto.ProveedorNombre` con `core.proveedores` mediante normalización segura de nombres.
- Reabastecimiento muestra proveedor maestro, OTIF, plazo de crédito y contacto cuando existe vínculo.
- La creación de PO exige seleccionar un proveedor activo del maestro corporativo.
- El backend valida `ProveedorId` y usa el nombre/razón social oficial del maestro; ya no confía en un nombre libre enviado por frontend.
- La pantalla de Compras carga proveedores reales y propone términos de pago según `PlazoCreditoDias`.
- Los nombres de proveedor del catálogo que no coinciden con el maestro se muestran como **no vinculados** para depuración de datos.
- Se agregan pruebas unitarias para la normalización/vinculación de nombres.

## No cambia

- No se crea migración.
- No se ejecuta `database update`.
- No se crean proveedores automáticamente.
- No se modifica `CodigoEAN`.
- Finanzas sigue siendo dueña de las PO y pagos.
- Core sigue siendo dueño del maestro de proveedores.

## Aplicación

Copiar el contenido del ZIP sobre la raíz de `ERP-Log-stica-` aceptando reemplazos.

Validar frontend:

```powershell
cd frontend\marketplace-ui
npm.cmd run build
cd ..\..
```

Validar backend:

```powershell
dotnet build MarketplaceERP.sln
dotnet test MarketplaceERP.sln
```

Se agregan 4 pruebas de Core. El criterio obligatorio es `Failed: 0`.

## Consideración local

Si `core.proveedores` está vacío, la pantalla mostrará 0 proveedores y Compras no permitirá crear una PO con un proveedor ficticio. Esto es intencional: la carga/administración del maestro corresponde al módulo Core/Compras.

## Commit sugerido

```powershell
git add .
git commit -m "Fase 5D: integrar maestro de proveedores con reabastecimiento y compras"
git status
```
