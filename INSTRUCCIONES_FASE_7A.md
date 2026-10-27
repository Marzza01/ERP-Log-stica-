# LOGIMKT — Fase 7A: limpieza de mocks y fallbacks del Grupo A

Fecha: 2026-10-27

## Objetivo
Eliminar los fallbacks visuales/locales que podían ocultar fallos reales de API en los flujos logísticos del Grupo A.

## Archivos modificados
- `frontend/marketplace-ui/src/context/AuthContext.tsx`
- `frontend/marketplace-ui/src/pages/productos/ProductosPage.tsx`
- `frontend/marketplace-ui/src/pages/wms/WmsPage.tsx`
- `frontend/marketplace-ui/src/pages/fulfillment/FulfillmentPage.tsx`
- `frontend/marketplace-ui/src/pages/rma/RmaPage.tsx`
- `frontend/marketplace-ui/src/pages/compras/ComprasPage.tsx`

## Cambios principales
1. Autenticación frontend
   - Se elimina el token administrativo automático.
   - Se elimina el login fallback que aceptaba prácticamente cualquier credencial.
   - El frontend solo considera sesión válida cuando existe una sesión real guardada por un login exitoso de API.

2. Productos
   - Se eliminan los 25 productos demo en caso de caída de API.
   - Crear/editar/dar de baja ya no simula éxito local.
   - En fallo se mantiene el estado real y se muestra error con opción Reintentar.
   - Nuevo SKU abre vacío, sin generar un SKU aleatorio de demostración.

3. WMS
   - Se elimina el mapa de 40 ubicaciones generadas localmente.
   - Si la API no responde, el mapa queda vacío y muestra error real.

4. Fulfillment
   - Se eliminan PickLists/paquetes demo.
   - Confirmar picking ya no actualiza la UI si el backend rechaza la acción.
   - Empacar ya no genera guía/paquete local cuando la API falla.
   - Tras éxito se recarga desde la API para conservar el estado de servidor.

5. RMA
   - Se elimina el RMA demo.
   - Los UUID de orden/producto ya no vienen precargados como datos falsos.
   - Se elimina el KPI de tasa calculado con un denominador ficticio; se sustituye por casos completados reales.
   - Los fallos de carga/registro se muestran explícitamente.

6. Compras
   - Se eliminan PO y 3-Way Match demo.
   - Registrar factura ya no simula éxito local.
   - 3-Way Match ya no calcula un resultado local ficticio si falla la API.
   - Los formularios dejan de venir precargados con SKU, factura, importes o UUID de demostración.

## Qué NO se modifica en 7A
- Contabilidad y Liquidaciones: contienen mocks, pero pertenecen a otros bloques/equipos.
- Ordenes: mantiene datos locales/demo heredados; requiere coordinación por ser flujo compartido/OMS.
- `CommissionServiceMock` y `PagoServiceMock`: pertenecen a Core/Finance y no se reemplazan desde Grupo A.
- `StockServiceMock`: queda como legado para pruebas aisladas; la DI productiva ya usa `StockService` real.
- Dashboard y Topbar: sus datos/notificaciones demo quedan para 7B.
- Backend de autenticación: aún usa usuarios de desarrollo en memoria; se documenta para 7B/handoff compartido.
- `CodigoEAN`: intacto.

## Validación
Desde la raíz del repositorio:

```powershell
cd frontend\marketplace-ui
npm.cmd run build
cd ..\..

dotnet build MarketplaceERP.sln
dotnet test MarketplaceERP.sln
```

Esperado:
- Frontend: TypeScript 0 errores y Vite `built`.
- Backend: `0 Error(s)`.
- Tests: Core 10/10, Catalog 55/55, Finance 22/22; total 87/87.

No hay cambios de modelo EF ni migraciones.

## Commit recomendado
```powershell
git add .
git commit -m "Fase 7A: eliminar mocks y fallbacks logísticos del frontend"
git status
```
