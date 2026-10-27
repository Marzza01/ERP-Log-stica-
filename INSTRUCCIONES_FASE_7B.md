# LOGIMKT — Fase 7B: UI compartida y optimización del frontend

## Objetivo
Eliminar datos simulados que aún quedaban en la UI compartida y reducir el bundle inicial sin tocar reglas de negocio, base de datos ni módulos financieros ajenos al Grupo A.

## Cambios incluidos

### Dashboard
- Se elimina por completo el fallback de KPI/modelo financiero con datos demo.
- Si la API falla, se muestran valores `—` y un mensaje de error; no se inventan números.
- Se eliminan textos estáticos como `+8.4%` y `100% cobrable`.
- KPI y modelo financiero se cargan de forma independiente con `Promise.allSettled`.
- Las tarjetas no abren detalle cuando no existen datos reales cargados.

### Topbar
- Se eliminan las notificaciones simuladas.
- La campana consume `GET /api/alertas-inventario` y muestra hasta 5 alertas `OPEN`.
- El contador representa el total real de alertas abiertas.
- Si la API de alertas falla, se informa el error en vez de mostrar falsamente que no existen alertas.
- Se elimina el botón de búsqueda sin implementación.
- El health check usa `/health` del mismo origen/proxy; no intenta `localhost:5000` desde el navegador en producción.
- Se agregan títulos para Ajustes, Conteos, Alertas y Bloqueos.

### Sidebar
- Se eliminan badges/contadores hardcodeados de Órdenes, WMS, RMA, Compras y Liquidaciones.
- No se modifica la estructura de módulos de otros equipos.

### Carga y bundle
- `App.tsx` usa `React.lazy` + `Suspense` para cargar páginas por ruta.
- `vite.config.ts` separa React, router, Recharts e iconos en chunks dedicados.
- El objetivo es reducir el chunk inicial y eliminar/reducir la advertencia >500 kB. El resultado definitivo debe comprobarse en el equipo local.

## Sin cambios de base de datos
No ejecutar:

```powershell
dotnet ef migrations add ...
dotnet ef database update
```

## Aplicación
Copiar el contenido del ZIP sobre la raíz de `ERP-Log-stica-`.

## Validación

```powershell
cd frontend\marketplace-ui
npm.cmd run build
cd ..\..

dotnet build MarketplaceERP.sln
dotnet test MarketplaceERP.sln
```

Criterios:
- TypeScript: 0 errores.
- Vite: `built`.
- Idealmente desaparece el warning de chunk >500 kB; si permanece, compartir la lista de tamaños para ajustar 7B sin afectar funcionalidad.
- .NET: 0 errores.
- Tests: `Failed: 0`.

## Commit recomendado

```powershell
git add .
git commit -m "Fase 7B: limpiar UI compartida y optimizar carga frontend"
git status
```
