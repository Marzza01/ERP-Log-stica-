# Evidencia local de respaldo y worktrees — 8 nov 2026

Información de la consola PowerShell enviada por el desarrollador; validada contra la historia de GitHub mediante comparaciones de commits. **No implica que los datos de producción, secretos o archivos ignorados estén respaldados.**

## Respaldo de archivos no confirmados

- Diez worktrees inspeccionados; **32 archivos no confirmados** copiados a carpeta externa y cada copia comparada por SHA-256.
- Distribución: principal 12, staging-500 2, backend-integ 16, frontend-integ 0, P0 0, P1 1, P5-Backup 1, P5-Security 0, vercel-patch 0, main_check 0.
- Los dos archivos `frontend/marketplace-ui/.gitignore` sin seguimiento en worktrees P1 y P5-Backup también fueron copiados.
- Se observan 5 worktrees con cambios y 5 limpios. El reporte local no incluye archivos ignorados por Git; confirmar manualmente si éstos guardan datos de valor.

## Git Bundle recuperable

- Archivo externo: `Historial_Git_20261108_134018/MarketplaceERP_Completo.bundle` (en carpeta privada; **no subir el bundle ni secretos al repositorio público**).
- SHA-256 comunicado: `81206BE6D68147C939C686A92916251F8BDEAD294E89F170A1CD561A8C53FABE`.
- `git bundle create --all` incluyó **123 refs**. `git bundle verify` devolvió `is okay`.
- `git clone --mirror` a una carpeta independiente terminó sin error.
- Cuatro SHA de `detached HEAD` protegidos con `refs/backup/worktrees-20261108/*` y verificados en el clon restaurado.

| Worktree | SHA protegido | Comparación GitHub con staging actual |
|---|---|---|
| P0 | `e79ad460dab68af6a2676a44824fc9bce6dee028` | Staging está 56 commits adelante y **0 detrás** |
| P1 | `f8b7c4e6926af7a55606680edb0363612dcc3656` | Staging está 48 adelante y **0 detrás** |
| P5-Backup | `b3d8f1fa87ed2fb12085bb456d5dc4c889c1b273` | Staging está 7 adelante y **0 detrás** |
| Main-Check | `45fcd8f6b89ead52f6dbd49ef099b68b9dc26456` | Staging está 189 adelante y **0 detrás** |

**Conclusión restringida:** los commits HEAD de los cuatro worktrees `detached` son ancestros de staging; no contienen commits exclusivos pendientes de integrar. Esto NO asegura que sus archivos no confirmados hayan sido recuperados funcionalmente: P1 y P5-Backup tenían cada uno un `.gitignore` sin seguimiento y éstos quedaron respaldados por separado.

## Siguiente gate: comparación byte/blob a la versión candidata

- `scripts/analizar-cambios-worktrees.ps1`: solo lectura sobre todos los worktrees; compara los 32 archivos modificados/nuevos contra `origin/consolidacion/erp-segura-20261108` y `origin/staging/catalog-500-neon-test`.
- Exporta SHA de cada archivo normalizado por filtros Git y clasificación (idéntico / diferente / nuevo).
- Conservar trabajos locales hasta **revisar diff semántico**. Coincidencia de blob implica contenido idéntico; diferencia de blob NO implica novedad funcional.
- **Ejecutado correctamente en Windows el 2026-11-08**, con salida `Comparacion_Worktrees_20261108-202734`. Resumen: **32 ocurrencias de archivos locales**; **7** blobs idénticos a la consolidación, **0** idénticos solo a staging, **16** distintos de ambas ramas, **9** ausentes de ambas ramas y **0** eliminados localmente. Esta clasificación es de contenido, no de equivalencia funcional.
- Por worktree: `MarketplaceERP`: 7 idénticos / 4 distintos / 1 ausente; `MarketplaceERP-staging-500`: 0 / 2 / 0; `MarketplaceERP-backend-integ`: 2 / 10 / 4; `MarketplaceERP-P1`: 0 / 0 / 1; `MarketplaceERP-P5-Backup`: 0 / 0 / 1; los restantes cinco worktrees están limpios. 
- **Pendiente:** incorporar el listado de rutas/estados del `Archivos_Comparados.csv` local para determinar los 16 archivos distintos y los 9 ausentes; no inferir qué contenido se puede descartar sin ese listado y revisión semántica. GitHub no puede ver estos archivos locales sin que el usuario los comparta.

## Límites de recuperación

Un bundle Git + copia de archivos modificados no equivale a `pg_dump` verificado/restaurado; Neon producción todavía exige respaldo independiente de esquema y datos. Tampoco se han comprobado archivos ignorados por Git, variables de entorno ni posibles secrets locales. No eliminar ramas ni worktrees hasta resolver la matriz local y los gates de producción.
