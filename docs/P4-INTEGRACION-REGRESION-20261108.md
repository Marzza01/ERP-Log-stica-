# P4 — Integración, regresión y entrega controlada (2026-11-08 UTC)

## Objetivo y alcance
Integrar P2 y P3 **únicamente en la rama Git de staging**, comprobar sus resultados y preparar promoción a main de forma segura. No autoriza modificar ni desplegar producción ni ejecutar pagos. Las ramas aceptadas de pruebas y la base de Neon staging se mantienen sin escrituras financieras.

## Línea de tiempo y rollback de código
- SHA de staging antes de P4: `bdee541484653021272fd63400c0908aa078f569`.
- Backup inmutable en Git: `backup/p4-staging-preintegracion-20261108`.
- PR #54 (P2) mergeado en staging, SHA merge `ba9756adb6592d81ec72f833853798b504570bc5`; CI aprobado del head P2 `d217a07cf9a08d91832893e19c810e4d6799218d`.
- PR #55 (P3) retarget de base de P2 a staging; mergeado en staging SHA `94fcb91b55bcaab6df745977f074cab5c1d7bc98`; CI aprobado del head P3 `e4735394fcc974dff4602a2407f410bb099c5611` (run #42437680956).
- Diferencia desde backup a staging después de ambos merges: 33 commits, **17 archivos**; cambios de frontend, pruebas, documentación y lectura de Productos, **ninguna modificación de migraciones ni lógica de pagos, conciliación o liquidaciones**.
- **No restaurar a ciegas:** para revertir código, crear un PR revert de los merges hacia staging, verificando el nuevo CI; no forzar rama ni restituir base de datos por un merge de código.

## Regresión real posterior a merges
- Ejecutado el SQL read-only `scripts/p1-staging-integrity.sql` contra la rama Neon staging `br-delicate-bar-bbjeww67`, base `marketplaceerp_test`, después de ambos merges. Los bloques de anomalías 10 + 12 + 9 = **31 controles con 0 violaciones**. Bloques de historia EF y conteos financieros ejecutados correctamente. No se ejecutó DML ni DDL.
- La prueba SQL **no demuestra transacciones nuevas en staging ni todos los endpoints**; varias tablas transaccionales están vacías.
- CI #42437680956 del head de P3 ya probó .NET 8, Node 24, Chromium y los 6 tests P3 con éxito, incluso 10k SKU simulados en UI, JWT de roles con API real y PostgreSQL efímero; **no** equivale a un E2E del nuevo SHA mergeado ni a una carga PostgreSQL de 10k.
- **Control adicional incorporado por P4:** CI workflow incluye `push: 'staging/**'`, antes ausente; tras integrar esta rama P4 ejecutar y verificar los 3 jobs contra el SHA exacto de staging. El cierre del bloque está condicionado a ese resultado.

## Estado del hosting
- Render staging `marketplaceerp-api-staging` apunta a rama `staging/catalog-500-neon-test`, pero **autoDeploy no**. No se ha desplegado el nuevo código con el merge P4. La API de staging existente puede seguir sirviendo la versión anterior.
- Render producción `marketplaceerp-api` usa rama **diferente** (`integracion/main-reconciliacion-20261102`) con autoDeploy; no modificarla.
- Intento de `Render.list_logs` en el servicio staging para niveles error/fatal: proveedor devolvió **503** (`failed to query loki: log retrieval is currently unavailable`). No inventar un resultado de logs. Se obtuvieron algunas métricas de memoria, pero ninguna serie de latencia o solicitudes en la ventana consultada. Reintentar cuando vuelva la disponibilidad.
- Vercel Preview y API Render aún requieren validación real con el SHA P4 tras **un despliegue explícitamente aprobado**; no tratar CI local como despliegue.

## Estado de riesgos
- Issue #51 abierto: 2 high y 7 moderate de dependencias npm de desarrollo según clasificación P1, 0 alertas runtime en aquella evaluación. Sigue pendiente remediación o aceptación de riesgo actualizada.
- PR #2 antiguo permanece abierto y en borrador; no integrarlo en main sin reconciliar divergencias.
- `main...staging` tras merges P2/P3: staging **171 commits por delante**, 0 detrás, **147 archivos** distintos. **No promover main automáticamente**. Analizar las migraciones de Core/Catalog/Finance contra el estado de DB destino y probar restore independiente.
- Recuperación de Neon por duplicación de ramas mostró 37/37 tablas coincidentes en P2; **no reemplaza** restauración independiente desde backup o snapshot.

## Criterios P4 y decisión
| Criterio | Estado objetivo |
|---|---|
| P4.1 P2 y P3 integrados en staging | APROBADO — merges #54 y #55 verificados |
| P4.2 CI del SHA de staging después de merges | PENDIENTE hasta finalizar CI del merge de este PR (workflow staging push incorporado) |
| P4.3 Integridad relacional de staging | APROBADO en 31 consultas sin anomalías, con alcance read-only |
| P4.4 Observabilidad del servicio | PARCIAL; Render logs 503, autoDeploy staging off |
| P4.5 Promoción a main/producción | BLOQUEADA; requiere evaluación separada, snapshot/restauración independiente y autorización |

**P4 de integración Git en staging** puede aceptarse con P4.1–P4.3 completos; **P4 de entrega productiva integral no puede declararse al 100 %** hasta completar P4.4–P4.5. Preservar esta distinción en el acta y en la presentación académica.
