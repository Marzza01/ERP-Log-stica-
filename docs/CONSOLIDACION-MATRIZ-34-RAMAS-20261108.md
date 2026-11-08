# Matriz de 34 ramas divergentes — MarketplaceERP

**Fecha:** 2026-11-08. **Base inmutable:** `staging/catalog-500-neon-test` @ `2d1c37748fe645d75c16168dd1d3b6214285bcea`.

**Regla de seguridad:** esta matriz clasifica historia Git y riesgo de cambios, NO certifica equivalencia funcional. No eliminar ni fusionar ramas por el mero número de commits exclusivos. Los `ahead` son frente a la base antes de esta rama de consolidación.

| # | Rama divergente | Commits exclusivos | Valor/riesgo identificado | Decisión preliminar |
|---:|---|---:|---|---|
| 1 | `backup/fase1-antes-fin02-20261106` | 5 | Respaldo histórico | Conservar hasta cierre; no fusionar |
| 2 | `backup/fase1-antes-fin03-20261106` | 6 | Respaldo histórico | Conservar hasta cierre; no fusionar |
| 3 | `backup/fase1-antes-reconciliar-staging-20261106` | 1 | Respaldo histórico | Conservar hasta cierre; no fusionar |
| 4 | `backup/fase1-pre-fin04-fin05-20261106` | 7 | Respaldo histórico | Conservar hasta cierre; no fusionar |
| 5 | `backup/fase1-pre-fin06-20261107` | 8 | Respaldo histórico | Conservar hasta cierre; no fusionar |
| 6 | `backup/fase1-pre-fin07-20261107` | 9 | Respaldo histórico | Conservar hasta cierre; no fusionar |
| 7 | `backup/fase1-pre-sec06-roles-20261106` | 4 | Respaldo histórico | Conservar hasta cierre; no fusionar |
| 8 | `backup/fase1-pre-smoke-migraciones-20261106` | 3 | Respaldo histórico | Conservar hasta cierre; no fusionar |
| 9 | `demo-web-logimkt` | 1 | Despliegue académico/pasarela deshabilitada | Revisar política; preservar guardias de staging |
| 10 | `fix/auditoria` | 27 | Checkout multiitem, secuencias, migraciones, contabilidad | Recuperación selectiva tras tests; NO merge total |
| 11 | `fix/compras-factura-requiere-po-enviada-20261107` | 2 | Reglas PO facturable | Probar equivalencia; no reintroducir versión anterior |
| 12 | `fix/compras-serializar-recepcion-20261107` | 3 | Bloqueo en recepciones | Prueba ya conservada; no merge |
| 13 | `fix/compras-verificar-reintentos-inbound-20261107` | 4 | Idempotencia recepción | Contrastar contrato; no merge |
| 14 | `fix/evaluacion-po-sku-duplicados-20261107` | 4 | Validación SKU por PO | Prueba conservada; no merge |
| 15 | `fix/fin07-detalle-factura-estado-po-20261107` | 2 | Validación factura-PO | Prueba conservada; no merge |
| 16 | `fix/fin07-preservar-fecha-recepcion-20261107` | 2 | Fecha recepción / 3-way | Prueba conservada; no merge |
| 17 | `fix/p0-guardrail-postgres-ci-20261107` | 1 | Protección tests PostgreSQL | Conservar guardrail staging; no merge |
| 18 | `fix/p0-integracion-postgres-20261107` | 14 | Test HTTP y compras reales | Comparar pruebas, evitar degradación |
| 19 | `fix/p0-payment-guard-http-20261107` | 2 | Autorización HTTP pagos | Comparar tests; no merge indiscriminado |
| 20 | `fix/p0-staging-acceptance-runner-20261107` | 3 | Runner aceptación staging | Validar equivalencia del runner actual |
| 21 | `fix/p1-audit-deps-staging-20261108` | 2 | Seguridad dependencias dev | Comparar docs; no overwrite |
| 22 | `fix/p1-browser-auth-lifecycle-20261107` | 17 | Autenticación React/Chromium | Conservar expiración estricta staging |
| 23 | `fix/p1-disable-seed-prod-20261107` | 2 | Seed demo restringido | Conservar bloqueo staging |
| 24 | `fix/p1-e2e-legacy-migrations-20261107` | 1 | Migraciones antiguas en tests | Comparar cobertura |
| 25 | `fix/p1-integrity-normalized-invoices-20261107` | 4 | Integridad facturas/proxy | Conservar hardening staging |
| 26 | `fix/p1-liquidaciones-semantica-staging-20261107` | 7 | Liquidación simulada / controles | Conservar transparencia de pagos |
| 27 | `fix/p1-ready-schema-bootstrap-tests-20261107` | 4 | Ready y bootstrap | Conservar readiness staging |
| 28 | `fix/p1-real-preview-e2e-runner-20261107` | 5 | E2E preview | Verificar runner y restricciones |
| 29 | `fix/p1-staging-readonly-acceptance-runner-20261107` | 4 | Smoke solo lectura | Mantener, verificar equivalencia |
| 30 | `fix/recepcion-po-persistencia-parcial-20261107` | 2 | Recepción parcial | Prueba conservada; no merge |
| 31 | `fix/reconciliar-staging-a-fase1-20261106` | 2 | Historia de reconciliación | Sin archivos en compare; no merge |
| 32 | `fix/staging-catalog-pagination-20261106` | 4 | Paginación catálogo | Verificar cobertura; no revertir staging |
| 33 | `integracion/fase1-seguridad` | 10 | Implementaciones alternativas seguridad | Comparación semántica; mantener staging |
| 34 | `integracion-main-auditoria` | 28 | Checkout multiitem y numeración histórica | Recuperación selectiva; NO merge total |

## Estado resumido

- 88 ramas originales: 53 ya contenidas en la historia de staging; 34 divergentes; 1 idéntica.
- Nuevas referencias de recuperación (fuera de este conteo histórico): `archive/preconsolidacion-main-20261108`, `archive/preconsolidacion-staging-20261108`, `archive/preconsolidacion-render-prod-20261108`, `archive/preconsolidacion-render-staging-20261108`.
- Rama aislada: `consolidacion/erp-segura-20261108`.
- **No borrar** `integracion/main-reconciliacion-20261102`: Render producción todavía la utiliza. No modificar `main` sin desacoplar Vercel Production.
- Históricas de alto valor: checkout multiproducto, numeración única/series, filtros de concurrencia, recuperación reservas y pruebas PostgreSQL. Reimplementación controlada cuando corresponda; nunca copiar migraciones antiguas sobre un esquema avanzado.

## Requisitos antes de declarar una rama eliminable

1. Confirmar que ningún despliegue, workflow, worktree o PR la utiliza.
2. Comparar árbol, patches semánticos, tests y migraciones de cada diferencia exclusiva.
3. Añadir evidencia de que la capacidad está conservada, reemplazada con pruebas o descartada justificadamente.
4. Preservar una referencia o tag de recuperación y registrar SHA.
5. Contar con CI de la candidata y autorización de limpieza por lote.

## Puertas antes de promoción

No promover a `main` ni ejecutar migraciones productivas hasta superar tests P0 OMS/WMS/Finance, concurrencia real, restauración de base, aceptación de rúbrica y revisión de auto-deploy Render/Vercel. PR #60 sigue NO MERGE.
