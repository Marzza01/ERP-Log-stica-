# Puertas de release, recuperación y poda — ERP Marketplace (2026-11-08)

**NO EJECUTAR MERGE / DELETE** hasta satisfacer y firmar las puertas que siguen. Este documento es un runbook, no evidencia de finalización.

## 1. Mapa real de dependencias (verificado por conectores)

| Sistema | Proyecto/recurso | Rama | Condición |
|---|---|---|---|
| GitHub | default | `main` | Sin rulesets / protección; SHA base `e04c90696e59d86390216a8b51bf00e876cfebc5` |
| GitHub | staging vigente | `staging/catalog-500-neon-test` | SHA `2d1c37748fe645d75c16168dd1d3b6214285bcea` |
| GitHub | PR #64 draft | `consolidacion/erp-segura-20261108` → staging | NO MERGE antes de pruebas críticas |
| GitHub | PR #60 draft | staging → `main` | NO MERGE; Vercel Production sigue main |
| Render | `srv-dbl9stegekts73foqer0` | `integracion/main-reconciliacion-20261102` | **autoDeploy ON**; NO eliminar rama |
| Render | `srv-dbmnd6jrjlhs73c0jung` | `staging/catalog-500-neon-test` | autoDeploy OFF; rollout manual aparte |
| Vercel | `prj_zmOkd2DQ4NtTqAFi9Bv7RFbGQp82` Production | `main` | Deploy automático desde commits; promoción cambia frontend |
| Vercel | Preview | `consolidacion/erp-segura-20261108` | READY en varios commits de prueba; no equivale a Production |
| Neon | `square-lake-18241058` | `main` y ramas de ensayo | No confundir branching con respaldo externo |

## 2. Antes de borrar cualquier rama

- [ ] Censo remoto exacto: `docs/CONSOLIDACION-CENSO-SHA-93-RAMAS-20261108.md`.
- [ ] Matriz de diferencias: `docs/CONSOLIDACION-MATRIZ-34-RAMAS-20261108.md`.
- [ ] Worktrees locales **de todos los equipos**: ejecutar `scripts/consolidacion-preflight.ps1` en cada clon relevante y preservar salida fuera del repo.
- [ ] Comprobar PR abiertos y workflows que refieran la rama; los actuales #60 y #64 siguen drafts.
- [ ] Respaldo externo `git bundle` con SHA256 y `git bundle verify`: ejecutar `scripts/consolidacion-backup-bundle.ps1` fuera del repo y ensayar clon en ubicación separada.
- [ ] Diferencias históricas con impacto semántico/negocio revisadas y documentadas, especialmente `fix/auditoria` e `integracion-main-auditoria`.
- [ ] Nunca confundir una rama **sin diferencias de archivos** con una referencia que pueda eliminarse sin verificar trabajo asociado.
- [ ] Para cada rama seleccionada: registrar `nombre + SHA + motivación + PR asociadas + test + respaldo + resultado de inspección de worktrees`.
- [ ] Crear tags de archivo persistentes y validados antes de eliminar ref remota; **no reescribir historia**.
- [ ] Borrar solo lotes pequeños de ramas contenidas que no dependan de ningún servicio; verificar después que staging/CI/Preview siguen igual.
- [ ] No usar comandos `--force`, `--prune`, ni eliminar ramas protegidas/de despliegue automáticamente.

## 3. Protección de main y staging — permisos administrativos pendientes

La API del conector devuelve HTTP 403 al leer branch protection y no permite configurar reglas. Un administrador debe configurar GitHub en **Settings → Rules → Rulesets**:

1. Crear regla que cubra `main` (y política apropiada para staging).
2. Exigir PR antes de actualizar; impedir force pushes y eliminación.
3. Exigir que CI pase: `Build & Test (.NET 8)`, `Build Frontend (Node 24)`, `E2E Browser (isolated Chromium)` (comprobar nombres reales en Checks).
4. Mantener rama actualizada y sin conversaciones sin resolver antes del merge.
5. Revisar permisos de bypass teniendo en cuenta un repositorio universitario con autor principal; evitar requisito de aprobaciones imposible si solo hay un colaborador.
6. Probar que una actualización directa a `main` queda bloqueada **antes** de autorizar release.

## 4. Gates obligatorios para actualizar main

- [ ] No hay P0 críticos de identidad de orden, picking, duplicados de guías, RMA, liquidaciones y asiento de venta sin pruebas PostgreSQL.
- [ ] E2E B2B/B2C de compra a entrega/RMA, 5+5 escenarios, y 50 solicitudes concurrentes sobre base efímera local.
- [ ] Conciliación Finance/Catalog con caída/reintento y sin doble movimiento.
- [ ] Rúbrica verificada: `docs/RUBRICA-UNICAES-MATRIZ-CUMPLIMIENTO-20261108.md` (requisitos obligatorios y evidencias).
- [ ] `git bundle` externo **creado y restore ensayado**; snapshot independiente de PostgreSQL **restaurado** en destino ajeno a producción.
- [ ] Control de deploys: identificar trigger de Vercel Production; congelar promoción automática durante cambio y preparar rollback.
- [ ] Mantener Render producción en su rama actual hasta prueba explícita de API compatible y cutover.
- [ ] Variables de entorno productivas inspeccionadas solo en valores no secretos; pagos reales deshabilitados para demo.
- [ ] CI aprobada para **SHA exacto** que se fusionará, no un commit anterior.
- [ ] Autorización explícita de promoción productiva y ventana operativa.
- [ ] PR #60 actualizado tras PR #64, revisado con base real y sin divergencias nuevas.

## 5. Recuperación si ocurre un fallo

1. Detener nuevas promociones; NO reparar con force push ni desplegar nuevamente a ciegas.
2. Identificar SHA de versión anterior en `archive/preconsolidacion-*` y en censo.
3. Restaurar código mediante PR/rollback del proveedor de despliegue; no confundir rollback frontend Vercel con rollback API Render.
4. Para la BD, valorar cambios de esquema y realizar restore desde copia independiente **ensayada**. No hacer `dotnet ef database update <vieja>` sin analizar pérdidas de datos.
5. Confirmar integridad de checkout, stock y asientos con comprobaciones read-only de `scripts/consolidacion-integridad-readonly.sql` y fixtures no vacíos.
6. Registrar incidente, recuperaciones y nuevo SHA de referencia.

**Estado actual:** scripts de inventario y backup preparados, pero no ejecutados en equipos ni certificados como restore; la eliminación y promoción están suspendidas.
