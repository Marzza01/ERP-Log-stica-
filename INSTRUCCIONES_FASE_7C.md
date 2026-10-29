# LOGIMKT — Fase 7C: UX transversal y cierre visual del Grupo A

## Objetivo
Cerrar la Fase 7 con correcciones compartidas de navegación, tablas y modales, sin modificar reglas de negocio, contratos API ni base de datos.

## Cambios incluidos

### 1. DataTable
- La paginación se corrige automáticamente si una actualización o filtro reduce el número de páginas.
- Evita el caso visual de quedar en una página vacía aunque existan registros en páginas anteriores.
- El buscador conserva el atajo `/`, pero el placeholder se simplifica a `Buscar...` y se agregan etiquetas accesibles.

### 2. Modal
- `role="dialog"`, `aria-modal` y título enlazado mediante `aria-labelledby`.
- Al abrir, el foco se mueve al primer control interactivo disponible.
- Al cerrar, el foco vuelve al elemento desde el que se abrió el modal.
- La página de fondo deja de desplazarse mientras el modal está abierto.
- Escape continúa cerrando el modal y el botón de cierre tiene etiqueta accesible.

### 3. Sidebar móvil
- Al seleccionar una opción de navegación, el menú móvil se cierra inmediatamente.
- El comportamiento de escritorio permanece igual.

### 4. Topbar / alertas
- Cuando falla `/api/alertas-inventario`, el dropdown ofrece `Reintentar` sin necesidad de cambiar de ruta o recargar toda la aplicación.
- No se generan alertas simuladas ni se oculta el fallo de API.

## Fuera de alcance
- Sin cambios backend.
- Sin migraciones.
- Sin cambios en reglas de inventario.
- Sin cambios en Contabilidad/Liquidaciones.
- Sin cambios en `CodigoEAN` / códigos de barras.

## Aplicación
Copiar el contenido del ZIP sobre la raíz de `ERP-Log-stica-`, conservando las rutas.

## Validación

```powershell
cd frontend\marketplace-ui
npm.cmd run build
cd ..\..

dotnet build MarketplaceERP.sln
dotnet test MarketplaceERP.sln
```

Esperado:
- TypeScript: 0 errores.
- Vite: build correcto y sin chunk > 500 kB.
- .NET: 0 errores.
- Tests: Core 10/10, Catalog 55/55, Finance 22/22 (87/87) salvo que exista un cambio posterior legítimo en el repositorio.

## Validación manual breve
1. Abrir una tabla con varias páginas, ir a una página alta y aplicar un filtro que reduzca resultados: la página debe ajustarse automáticamente.
2. Abrir un modal desde un botón: el foco debe entrar al modal; al cerrarlo debe regresar al botón anterior.
3. En vista móvil, abrir Sidebar y seleccionar una ruta: el menú debe cerrarse.
4. Con API de alertas no disponible, abrir la campana y usar `Reintentar`.

## Commit sugerido

```powershell
git add .
git commit -m "Fase 7C: cerrar UX transversal del frontend"
git status
```
