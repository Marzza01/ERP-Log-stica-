# P2 — Auditoría de integridad y preparación de entrega (2026-11-08 UTC)

> **Estado: NO CERRADO AL 100 %.** Evidencia reproducible, alcance limitado. No confundir resultados CI y lectura de staging con aprobación productiva o transferencia bancaria real.

## Alcance y restricciones
Repositorio `Marzza01/ERP-Log-stica-`; base Git `staging/catalog-500-neon-test` SHA `bdee541484653021272fd63400c0908aa078f569`.
Neon proyecto `square-lake-18241058`; staging `br-delicate-bar-bbjeww67`, base `marketplaceerp_test`. Se creó **solo para ensayo aislado** la rama `br-polished-union-bbrwd0tu` (nombre `p2-ensayo-integridad-20261108`) a partir de staging. Ningún dato de staging, Neon main ni servicio de producción fue modificado por esta auditoría. Solo lecturas SQL en el clon. No pagos, merges ni deploys.

## Criterios de P2 y evidencia
| Criterio | Estado | Evidencia y pendiente |
| --- | --- | --- |
| P2.1 Builds/tests | **CI anterior aprobado; nuevo SHA sin checks aún** | PR #52 CI `42434016484`: .NET 474 (Core 10, Catalog 64, Finance 400), frontend y Chromium aprobados. La aceptación de P1 en Issue #45 corresponde a staging; confirmar CI para SHA de esta documentación antes de merge. |
| P2.2 Datos demo | **PARCIAL** | Datos disponibles y SQL abajo; faltan roles Vendedor/Comprador, órdenes de cliente, reservas, picking y liquidaciones de ensayo. |
| P2.3 Guion por módulo | **PREPARADO, NO EJECUTADO** | Ver `docs/P2-GUION-Y-RECUPERACION.md`. E2E real de Admin es solo lectura, no cubre todas las pantallas con fixtures. |
| P2.4 Configuración/recuperación | **PARCIAL** | 14 migraciones verificadas en clon. Hay backup branches, pero NO existe evidencia de restauración real ensayada con checksums o conteos pre/post. |
| P2.5 Integración segura | **PLAN DEFINIDO, NO AUTORIZADO** | `main...staging`: 138 commits adelante, 0 detrás, 134 archivos comparados. PR #2 sigue abierto; su rama diverge con staging (staging 20 commits adelante, 10 detrás en comparación entre ambas). Sin merge/deploy. |

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

## Puertas para cerrar P2 realmente al 100%
- [ ] Fixtures de demostración creados y probados **mediante APIs reales en sandbox**, nunca por inserciones directas que salten lógica de negocio. Identificar dataset y limpiarlo/archivarlo al cerrar.
- [ ] Usuarios canónicos Admin/Vendedor/Comprador, inventario disponible y orden completa de venta; evidencias con estados y cantidades, sin pagos reales.
- [ ] Guion P2.3 ejecutado en navegador con capturas, URL, SHA y resultados por módulo.
- [ ] Restauración efectiva de snapshot/backup en rama de ensayo independiente y comparación de migraciones, integridad, conteos y referencias; detener ante discrepancias.
- [ ] Estado Render y Vercel auditado en workspace autorizado; registrar errores relevantes y sus resoluciones.
- [ ] Documentos de estado actual contrastados con implementación; respaldo/rollback revisados.
- [ ] PR #2 resuelto explícitamente según diff, análisis de los 138 commits/migraciones y CI para el candidato final.
- [ ] Riesgos de Issue #51 remediados o aceptados de forma documentada según criterio de entrega; pruebas posteriores verdes.
- [ ] Checklist firmado con evidencias y SHA final, sin confundir aprobación de staging con producción.

## Decisión
**P2 no se certifica al 100 %.** Este documento aporta auditoría verificable y una lista de puertas de salida. No autoriza escribir datos persistentes en staging ni modificar producción.
