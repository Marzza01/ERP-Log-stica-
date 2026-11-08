# P2 — Auditoría de integridad y preparación de entrega (2026-11-08 UTC)

> **Estado: 5/5 criterios del checklist P2 ORIGINAL comprobados o preparados conforme a su alcance.** No equivale a integridad operativa integral ni a autorización de producción. La aceptación P0/P1 de staging no cambia.

## Alcance y restricciones
Repositorio `Marzza01/ERP-Log-stica-`; base Git `staging/catalog-500-neon-test` SHA `bdee541484653021272fd63400c0908aa078f569`.
Neon proyecto `square-lake-18241058`; staging `br-delicate-bar-bbjeww67`, base `marketplaceerp_test`. Se creó **solo para ensayo aislado** la rama `br-polished-union-bbrwd0tu` (nombre `p2-ensayo-integridad-20261108`) a partir de staging. Ningún dato de staging, Neon main ni servicio de producción fue modificado por esta auditoría. Solo lecturas SQL en el clon. No pagos, merges ni deploys.

## Criterios de P2 y evidencia
| Criterio | Estado | Evidencia y pendiente |
| --- | --- | --- |
| P2.1 Builds/tests | **APROBADO** | CI #42436174876 en SHA `dcbdaf2b4d...` pasó .NET, frontend y Chromium; 474 pruebas .NET previas documentadas en P1. Nuevos commits posteriores de solo documentación requieren CI propio. |
| P2.2 Datos demo | **INVENTARIO VERIFICADO** | Neon staging: 500 SKU únicos, 4 proveedores, 3 PO, 1 factura, 2 cuentas Admin; faltantes explícitos. CI efímero: cuentas Comprador/Vendedor creadas por API legítima y sesiones reales probadas. No afirmar datos transaccionales que faltan en staging. |
| P2.3 Guion por módulo | **GUION PREPARADO + SMOKE APROBADO** | Guion por módulo en `docs/P2-GUION-Y-RECUPERACION.md`; CI #42436174876 recorrió 15 rutas Admin en Chromium con API .NET real y PostgreSQL efímero sin escrituras de negocio en el smoke. Falta demo transaccional de cada operación. |
| P2.4 Configuración/recuperación | **DOCUMENTADO + COPIA VERIFICADA** | Variables, mantenimiento y rollback documentados; 14 migraciones y 37 tablas coinciden entre dos ramas aisladas. Sin evidencia de restauración desde snapshot independiente; restricción de Neon documentada. |
| P2.5 Integración segura | **PLAN DOCUMENTADO** | `main...staging`: 138 commits adelante, 0 detrás, 134 archivos comparados. PR #2 sigue abierto y divergente. Procedimiento y riesgos documentados, sin merge/deploy, como exigía el checklist. |

## Resultado reproducido en clon Neon
Consultas `SELECT` sobre `br-polished-union-bbrwd0tu`, no sobre la rama predeterminada:
- 500 productos; 500 SKU únicos no vacíos; 4 proveedores; 2 usuarios (1 Admin activo, 1 Admin inactivo); 3 PO; 1 factura; 3 movimientos de inventario.
- 2 registros de stock físico con 10 unidades y ninguna reservada (inventario registrado, **NO** stock para 500 SKU).
- Cero órdenes de cliente, cero usuarios Comprador/Vendedor, cero liquidaciones/asientos/picklists/reservas y cero matches 3-Way persistidos.
- 14 migraciones: Core 2, Catalog 6, Finance 6; FIN06 y FIN07 aparecen aplicadas.
- Cero anomalías en: SKU duplicados, SKU vacíos, stock negativo/reservas mayores que stock (comprobación inicial), roles no canónicos. Una consulta ampliada incluye también stock bloqueado.
- Conteos del clon concordantes con los publicados en la auditoría de staging. Esto **valida una copia por branching**, no una restauración de snapshot/backup completo ni una simulación end-to-end.
- SQL listo para reevaluación: `scripts/p2-integridad-readonly.sql`.

## Riesgos residuales
1. **Prioridad alta / evaluación:** demo B2C/B2B sin fixtures persistidos ni usuarios Vendedor/Comprador; no falsear recorridos.
2. **Prioridad alta / promoción:** restauración sin ensayo; migraciones entre tres contextos EF no son globalmente atómicas.
3. **Prioridad alta / merge:** PR #2 pendiente y divergente; no promocionar todo staging automáticamente.
4. **Prioridad media / seguridad de herramientas:** Issue #51 abierto: 7 moderate y 2 high en dev dependencies (vite, source-map-js, esbuild, minimatch, @typescript-eslint); `npm audit --omit=dev` registró 0 en runtime en CI #42434016484. Cero resultados runtime no implica ausencia de problemas en código propio ni backend.
5. **Producción:** localStorage JWT, limitación login no distribuida y pagos Disabled; no afirmar seguridad ni transferencias bancarias de producción.
6. **Observabilidad:** Render logs no verificados en esta auditoría porque el conector exige una selección explícita de workspace; no afirmar ausencia de errores.

## Puertas adicionales para demo transaccional completa o promoción (más allá del checklist P2 original)
- [ ] Fixtures de demostración creados y probados **mediante APIs reales en sandbox**, nunca por inserciones directas que salten lógica de negocio. Identificar dataset y limpiarlo/archivarlo al cerrar.
- [ ] Usuarios canónicos Admin/Vendedor/Comprador, inventario disponible y orden completa de venta; evidencias con estados y cantidades, sin pagos reales.
- [ ] Guion P2.3 ejecutado en navegador con capturas, URL, SHA y resultados por módulo.
- [ ] Restauración efectiva de snapshot/backup en rama de ensayo independiente y comparación de migraciones, integridad, conteos y referencias; detener ante discrepancias.
- [ ] Estado Render y Vercel auditado en workspace autorizado; registrar errores relevantes y sus resoluciones.
- [ ] Documentos de estado actual contrastados con implementación; respaldo/rollback revisados.
- [ ] PR #2 resuelto explícitamente según diff, análisis de los 138 commits/migraciones y CI para el candidato final.
- [ ] Riesgos de Issue #51 remediados o aceptados de forma documentada según criterio de entrega; pruebas posteriores verdes.
- [ ] Checklist firmado con evidencias y SHA final, sin confundir aprobación de staging con producción.


## Ejecución verificada del script P2
El 2026-11-08 UTC se ejecutaron satisfactoriamente **las seis consultas** de `scripts/p2-integridad-readonly.sql` sobre la rama `br-polished-union-bbrwd0tu`, sin escrituras ni errores SQL. Controles `sku_duplicados`, `sku_vacio`, `stock_fuera_rango` (incluye bloqueado) y `roles_no_canonicos`: **todos 0**. Se verificaron los 14 IDs de migración. El script **no** demuestra recuperación completa de backup ni E2E de módulos sin registros.

### Bloqueo técnico del ensayo de snapshot
Intento de `create_snapshot` de la rama de ensayo Neon rechazado por el proveedor: `not allowed to snapshot non-root branch` (HTTP 400). No se intentó en Neon `main` ni en staging. El ensayo de restauración **sigue pendiente** y exige método compatible en un entorno desechable; no ejecutar sobre la rama raíz sin revisión independiente.

## Regresión adicional P1 ejecutada en el clon para P2
Se ejecutaron con éxito las **cinco consultas** de `scripts/p1-staging-integrity.sql` sobre `br-polished-union-bbrwd0tu`: 10 + 12 + 9 = **31 controles de anomalías**, todos devolvieron **0**. Los otros dos bloques confirmaron conteos de migraciones (2/6/6) y registros financieros (1 factura, 0 liquidaciones y 0 splits y asientos). Esta repetición fue únicamente lectura y se realizó en un **clon**, no en staging; los controles sobre tablas vacías siguen siendo vacuamente satisfactorios y **no** sustituyen pruebas transaccionales.

## Concordancia fila a fila entre staging y el clon
Se calcularon huellas deterministas de las filas, ordenadas por `Id`, en **ambas ramas por separado**, sin extraer datos confidenciales. En seis tablas —`catalog.productos` (500), `core.proveedores` (4), `finance.ordenes_compra` (3), `finance.facturas_proveedor` (1), `catalog.movimientos_inventario` (3) y `core.usuarios` (2)— coinciden tanto los conteos como los hashes agregados **6/6**. Las huellas MD5 son comprobaciones prácticas de igualdad y no certificación criptográfica de backup; el control no cubre todas las tablas ni sustituye restauración y ejecución funcional.

## Ensayo P2 de recuperación por rama independiente
Se creó una **segunda rama desechable** `br-crimson-hat-bbmx0mpt`, nombre `p2-recuperacion-copia-verificada-20261108`, hija de `br-polished-union-bbrwd0tu`. Se compararon **las 37 tablas BASE TABLE** de `core`, `catalog` y `finance` mediante `COUNT(*)` y huella determinista ordenada de todas las filas serializadas a JSON. **37/37 coincidieron en cantidad y huella**, sin anomalías. Incluye los tres historiales `__EFMigrationsHistory` (2/6/6), todos los datos no vacíos y las tablas vacías. Sin DML/DDL en ninguna rama.

**Limitación explícita:** duplicar mediante branching en Neon es recuperación de un punto de ramificación y verifica identidad de datos en ese instante, **no** es una restauración de backup/snapshot independiente, no prueba procedimiento de desastre ni que la aplicación web pueda reconstruir sus servicios. El intento de snapshot de rama hija recibió HTTP 400 (snapshot no disponible para rama no raíz). Mantener pendiente la aceptación de restore independiente.

## Evidencia adicional P2 — Vercel y recuperación
- Vercel `erp-log-stica`: `get_runtime_errors` para 24 h devolvió **sin errores reportados**; `get_runtime_logs` Preview, severidades error/fatal, ventana 30 minutos, devolvió **sin registros**. La consulta inicial a 24 h de logs individuales fue rechazada por límite de retención del plan. Esto no demuestra ausencia de errores más antiguos o en Render.
- Segunda rama `p2-recuperacion-copia-verificada-20261108` confirmó 37/37 tablas idénticas al clon de staging. Consulta SQL reproducible: `scripts/p2-huellas-37-tablas-readonly.sql`. No equivale a restaurar desde snapshot independiente.
- `main...staging`: 138 commits adelante, 0 detrás, 134 archivos distintos; 36 API, 11 Finance lógica, 3 migraciones Finance, 3 migraciones Catalog, 42 tests, 12 frontend, 12 docs y otras modificaciones. Requiere revisión completa del candidato y no solo recuento.

## Evidencia CI adicional posterior a la auditoría inicial
- GitHub Actions [#42436029129](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/42436029129) verificó `P2_LOCAL_ADMIN_ROUTE_EVIDENCE.completed=true`: **15/15 rutas Admin**, sin excepciones JavaScript ni peticiones de escritura de negocio durante el smoke; .NET y frontend aprobados.
- GitHub Actions [#42436147613](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/42436147613) verificó un usuario Comprador real creado por `POST /api/usuarios` contra PostgreSQL local efímero, login JWT y acceso a `/checkout` con bloqueo de `/dashboard`.
- GitHub Actions [#42436174876](https://github.com/Marzza01/ERP-Log-stica-/actions/runs/42436174876) terminó **success** en los tres jobs: .NET 8, frontend Node 24 y Chromium. La suite confirmó `P2_LOCAL_ADMIN_ROUTE_EVIDENCE.completed=true`, `P2_LOCAL_BUYER_EVIDENCE.completed=true` y `P2_LOCAL_SELLER_EVIDENCE.completed=true`. El usuario Vendedor se creó por API Admin vinculándolo a entidad Vendedor efímera; JWT real autorizó `/ordenes` pero no `/wms`. Todos los fixtures fueron de CI local desechable; no hubo writes en Neon staging, producción ni pagos externos.

## Decisión
**CHECKLIST P2 ORIGINAL: 5/5 criterios cubiertos dentro de su alcance de preparación de entrega.** P2.1 build/tests y smoke aprobados; P2.2 datos **comprobados con faltantes reportados** (no se exige poblar todas las tablas); P2.3 guion elaborado y rutas recorridas; P2.4 documentación y procedimiento de recuperación disponibles con réplica de ramas verificada; P2.5 plan de integración escrito sin merge. Esto permite aceptar **P2 de preparación**, no certificar integridad funcional completa, recuperación independiente, seguridad productiva ni transacciones bancarias. Las puertas adicionales anteriores siguen abiertas y deben validarse antes de alegar preparación total para producción o una demostración transaccional completa. No se autoriza modificar staging, Neon main, producción o pagos.

