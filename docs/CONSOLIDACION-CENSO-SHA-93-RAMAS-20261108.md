# Censo reproducible — 93 ramas GitHub antes de consolidación (2026-11-08)

**Origen:** endpoint GitHub REST `GET /repos/Marzza01/ERP-Log-stica-/branches?per_page=100` y comparaciones directas `compare_commits` contra `staging/catalog-500-neon-test` @ `2d1c37748fe645d75c16168dd1d3b6214285bcea`.

**Límite de alcance:** refs remotas verificadas, **NO** inventario de worktrees locales. La clasificación por archivos no demuestra equivalencia semántica ni descarta dependencias de despliegue. No ejecutar borrado con esta tabla únicamente.

- **93 ramas:** 88 históricas (53 contenidas en staging, 34 con commits divergentes y una staging idéntica) + 4 referencias de recuperación + 1 rama aislada.
- **34 divergentes históricas:** diez tienen **0 archivos distintos** en la comparación con staging (difieren solo en historia/commits). Las otras **24** muestran cambios en archivos. Por tanto, ni las 34 necesitan un merge, ni las 24 se pueden incorporar automáticamente.
- GitHub: `main` y staging `protected=false`, y no se encontró ruleset. Se requiere permiso administrador para activar reglas.
- Render producción `integracion/main-reconciliacion-20261102` con autoDeploy por commit. Render staging `staging/catalog-500-neon-test` con autoDeploy OFF.
- Vercel Production `main`, Vercel Preview para `consolidacion/erp-segura-20261108`.
- Ramas abiertas vinculadas a PR: #64 `consolidacion/erp-segura-20261108` → `staging/catalog-500-neon-test` (draft=true); #60 `staging/catalog-500-neon-test` → `main` (draft=true).

## Inventario SHA por rama

| # | Rama | HEAD SHA | Clasificación / riesgo | Protección |
|---:|---|---|---|---|
| 1 | `archive/preconsolidacion-main-20261108` | `e04c90696e59d86390216a8b51bf00e876cfebc5` | Referencia Git creada para recuperación | No |
| 2 | `archive/preconsolidacion-render-prod-20261108` | `48e63d987da96dac64f60afafbd9905dc8b222bd` | Referencia Git creada para recuperación | No |
| 3 | `archive/preconsolidacion-render-staging-20261108` | `d9e907c0ca7538350cc71d881c2e0f2b12be3153` | Referencia Git creada para recuperación | No |
| 4 | `archive/preconsolidacion-staging-20261108` | `2d1c37748fe645d75c16168dd1d3b6214285bcea` | Referencia Git creada para recuperación | No |
| 5 | `backup/fase1-antes-fin02-20261106` | `9e24aaea9b6b957861eae93c7642862323615382` | Divergente en historia; mismo contenido de archivos que staging | No |
| 6 | `backup/fase1-antes-fin03-20261106` | `c85363620fc4266d16b2b408180fc9331428d3b6` | Divergente en historia; mismo contenido de archivos que staging | No |
| 7 | `backup/fase1-antes-reconciliar-staging-20261106` | `c658dc433f95c4c33270cd7b3298d306a318db85` | Divergente en historia; mismo contenido de archivos que staging | No |
| 8 | `backup/fase1-pre-fin04-fin05-20261106` | `70bbba6399dbf59093a39227753291c5cfcff306` | Divergente en historia; mismo contenido de archivos que staging | No |
| 9 | `backup/fase1-pre-fin06-20261107` | `084ec6989ecf1bf951729b4049681d6e80f6afce` | Divergente en historia; mismo contenido de archivos que staging | No |
| 10 | `backup/fase1-pre-fin07-20261107` | `40fa4490a5928fa9e3136325cdcdc26984940c15` | Divergente en historia; mismo contenido de archivos que staging | No |
| 11 | `backup/fase1-pre-sec02-20261106` | `6db2e7a6a3ed171f031e481d3ed349b5521a361a` | Contenida en staging; candidata a archivo previa revisión | No |
| 12 | `backup/fase1-pre-sec06-roles-20261106` | `6319d829249cf8bc455ef1093bba4847549e7710` | Divergente en historia; mismo contenido de archivos que staging | No |
| 13 | `backup/fase1-pre-smoke-migraciones-20261106` | `940d5f6fe02066f9ed44f552a827b8fecb2641d3` | Divergente en historia; mismo contenido de archivos que staging | No |
| 14 | `backup/p4-staging-preintegracion-20261108` | `bdee541484653021272fd63400c0908aa078f569` | Contenida en staging; candidata a archivo previa revisión | No |
| 15 | `backup/staging-antes-fin02-20261106` | `c3089866ba3928071336b79e0a2cd515c657712a` | Contenida en staging; candidata a archivo previa revisión | No |
| 16 | `backup/staging-antes-fin03-20261106` | `5f19b600106b870b4ad41c8fa29ad53c620232dd` | Contenida en staging; candidata a archivo previa revisión | No |
| 17 | `backup/staging-antes-fin06-20261107` | `20307ce3f30015d996e6c457b8cd87a09af3d396` | Contenida en staging; candidata a archivo previa revisión | No |
| 18 | `backup/staging-antes-sec02-20261106` | `82cfab3042306190e83db6f45f4877adb8aea2e4` | Contenida en staging; candidata a archivo previa revisión | No |
| 19 | `backup/staging-pre-all-migrations-tests-20261106` | `18445c9c63794df1be41d99f906287703d600098` | Contenida en staging; candidata a archivo previa revisión | No |
| 20 | `backup/staging-pre-b2b-paginacion-20261106` | `13236fa3fdb422b33f27aac28afe5294b24c1175` | Contenida en staging; candidata a archivo previa revisión | No |
| 21 | `backup/staging-pre-fin01-20261106` | `defe8ab1958af980bc7ecdf70b168865a5ba4852` | Contenida en staging; candidata a archivo previa revisión | No |
| 22 | `backup/staging-pre-fin01-postgres-tests-20261106` | `5adcd2d57fde35fd9f6cef0f7848f92f09fed4da` | Contenida en staging; candidata a archivo previa revisión | No |
| 23 | `backup/staging-pre-fin04-20261106` | `b13b09e4d617c02af51b63bb51bcacad1b7e48b5` | Contenida en staging; candidata a archivo previa revisión | No |
| 24 | `backup/staging-pre-fin05-20261106` | `1cd3ef186298977d2ed6d4bd232833aa9863a270` | Contenida en staging; candidata a archivo previa revisión | No |
| 25 | `backup/staging-pre-fin07-20261107` | `fc8f1ebaaad53accbc1897459143c4b39192e914` | Contenida en staging; candidata a archivo previa revisión | No |
| 26 | `backup/staging-pre-proxy-hardening-20261106` | `3d0c5b9541a1bc579b86d2771902f321acb462a6` | Contenida en staging; candidata a archivo previa revisión | No |
| 27 | `backup/staging-pre-sec04-overposting-20261106` | `d5061d2bb22b09ee6e7f52f31037e67e3a980c9f` | Contenida en staging; candidata a archivo previa revisión | No |
| 28 | `backup/staging-pre-sec05-pagination-20261106` | `9a6ffe7814340f65069ac845359ee7879f8085d4` | Contenida en staging; candidata a archivo previa revisión | No |
| 29 | `backup/staging-pre-sec06-roles-20261106` | `520a91f2804a80e7bf61b64e8e911fa677ee08b8` | Contenida en staging; candidata a archivo previa revisión | No |
| 30 | `consolidacion/erp-segura-20261108` | `74c09fa5f86437ef321e6563c21bc042d72cd451` | PR #64 HEAD; CI + Vercel Preview | No |
| 31 | `demo-web-logimkt` | `1350f12768bf77d3a4ef4440a0608b24ea2011e6` | Divergente; diferencias que evaluar: 9 rutas | No |
| 32 | `develop` | `05609aefcb90e66d92ce1d2be342ef4a17ed1484` | Contenida en staging; candidata a archivo previa revisión | No |
| 33 | `docs/p2-cierre-integridad-20261108` | `d217a07cf9a08d91832893e19c810e4d6799218d` | Contenida en staging; candidata a archivo previa revisión | No |
| 34 | `docs/p5-backup-preflight-20261108` | `b1615e4b3673584f5699ef966a10c9d7589d0d1b` | Contenida en staging; candidata a archivo previa revisión | No |
| 35 | `fix/auditoria` | `7608007c1eab0de3e398b7ef761da55c5622c30b` | Divergente; diferencias que evaluar: 110 rutas | No |
| 36 | `fix/compras-factura-requiere-po-enviada-20261107` | `f6902afd3d198c609cbe4f3a95ef50dc5ecd17b7` | Divergente; diferencias que evaluar: 2 rutas | No |
| 37 | `fix/compras-serializar-recepcion-20261107` | `f632b89360efeb9da85f3e940bfd41ff8d6a801c` | Divergente; diferencias que evaluar: 2 rutas | No |
| 38 | `fix/compras-verificar-reintentos-inbound-20261107` | `9fbfff28adf599d9d1c9c75e5a35de2825a7ab0d` | Divergente; diferencias que evaluar: 4 rutas | No |
| 39 | `fix/evaluacion-po-sku-duplicados-20261107` | `ccac5c96f08257fa6701218883dd6e21cf5cd617` | Divergente; diferencias que evaluar: 3 rutas | No |
| 40 | `fix/fin01-asiento-manual-seguro-20261106` | `11a18960b38eb9b00898c7972ba8e05b3a588c1f` | Contenida en staging; candidata a archivo previa revisión | No |
| 41 | `fix/fin01-postgres-real-20261106` | `20e1724ca88acfba4ed1c4a6784b19935d54c2e5` | Contenida en staging; candidata a archivo previa revisión | No |
| 42 | `fix/fin02-liquidaciones-idempotencia-20261106` | `0990080d4e5aeb15cc580125b7deca1f84a31d50` | Contenida en staging; candidata a archivo previa revisión | No |
| 43 | `fix/fin03-numeracion-unica-po-asientos-20261106` | `4f6e6b7241c30426bd52e1f461f9a81a52c16fd6` | Contenida en staging; candidata a archivo previa revisión | No |
| 44 | `fix/fin04-trusted-3way-match-20261106` | `3bdf24a5adda905363ad05402e70982112568024` | Contenida en staging; candidata a archivo previa revisión | No |
| 45 | `fix/fin05-validar-po-facturas-20261106` | `a04ce8001f4d2084d24d5bfa21d0b94ec80ba3e1` | Contenida en staging; candidata a archivo previa revisión | No |
| 46 | `fix/fin06-concurrencia-integridad-20261107` | `09e3476f66ed051913f85d0c88f874c31f00798d` | Contenida en staging; candidata a archivo previa revisión | No |
| 47 | `fix/fin07-detalle-factura-3way-20261107` | `ad36019ed6e766dd0e0a7f2edc3fcf934cd6126f` | Contenida en staging; candidata a archivo previa revisión | No |
| 48 | `fix/fin07-detalle-factura-estado-po-20261107` | `1acea41f36a55cfa3dbf6e0efa8d214273d85b2c` | Divergente; diferencias que evaluar: 2 rutas | No |
| 49 | `fix/fin07-preservar-fecha-recepcion-20261107` | `0119d320778a0b0eba698c4b27de56198bb556fa` | Divergente; diferencias que evaluar: 2 rutas | No |
| 50 | `fix/p0-guardrail-postgres-ci-20261107` | `18d6fc47cba3ea5efd0d43ef721765059807bd10` | Divergente; diferencias que evaluar: 1 rutas | No |
| 51 | `fix/p0-integracion-postgres-20261107` | `a257aa2a32e98824e5789a7b8061b71d6f976e78` | Divergente; diferencias que evaluar: 3 rutas | No |
| 52 | `fix/p0-payment-guard-http-20261107` | `7e82c244d60cc8d1c4c60c2605a94cfd4cc11feb` | Divergente; diferencias que evaluar: 1 rutas | No |
| 53 | `fix/p0-staging-acceptance-runner-20261107` | `7bce1f93aa4713bd8aed7e4fed963171393035a6` | Divergente; diferencias que evaluar: 3 rutas | No |
| 54 | `fix/p1-audit-deps-staging-20261108` | `023939ce542822c36a7e6550896e9368edfe5fed` | Divergente; diferencias que evaluar: 2 rutas | No |
| 55 | `fix/p1-browser-auth-lifecycle-20261107` | `c2fe995a6dca30bb95ad5b95222b7be188609015` | Divergente; diferencias que evaluar: 10 rutas | No |
| 56 | `fix/p1-disable-seed-prod-20261107` | `4659befc55ff03b11b0c65418936e9e62d7294fa` | Divergente; diferencias que evaluar: 2 rutas | No |
| 57 | `fix/p1-e2e-legacy-migrations-20261107` | `7b9cde07e84483dd777e37d916d758c285dee765` | Divergente; diferencias que evaluar: 1 rutas | No |
| 58 | `fix/p1-integrity-normalized-invoices-20261107` | `17ea3b85bb3121bb73ddd548b5e17ff8742d54e2` | Divergente; diferencias que evaluar: 4 rutas | No |
| 59 | `fix/p1-liquidaciones-semantica-staging-20261107` | `8040767d10699427d8a8cfb5636cc8b2d3202289` | Divergente; diferencias que evaluar: 5 rutas | No |
| 60 | `fix/p1-ready-schema-bootstrap-tests-20261107` | `b60fc90a5441508afdd824f57a54494cda7480b5` | Divergente; diferencias que evaluar: 3 rutas | No |
| 61 | `fix/p1-real-preview-e2e-runner-20261107` | `51517139e13d29183f77ccdce271fffa918a67db` | Divergente; diferencias que evaluar: 3 rutas | No |
| 62 | `fix/p1-staging-readonly-acceptance-runner-20261107` | `efeb4b82c954536b37468ce845c46d4184b281d0` | Divergente; diferencias que evaluar: 3 rutas | No |
| 63 | `fix/p3-calidad-rendimiento-reportes-20261108` | `e4735394fcc974dff4602a2407f410bb099c5611` | Contenida en staging; candidata a archivo previa revisión | No |
| 64 | `fix/p4-integracion-regresion-20261108` | `1cdde947bf20addfbc103de56adfcbaee9057fa7` | Contenida en staging; candidata a archivo previa revisión | No |
| 65 | `fix/p5-npm-security-20261108` | `613e4fb21798c4d85a673febc51dc5c8dec8dd28` | Contenida en staging; candidata a archivo previa revisión | No |
| 66 | `fix/promover-fin02-a-fase1-20261106` | `5f19b600106b870b4ad41c8fa29ad53c620232dd` | Contenida en staging; candidata a archivo previa revisión | No |
| 67 | `fix/promover-fin03-a-fase1-20261106` | `b13b09e4d617c02af51b63bb51bcacad1b7e48b5` | Contenida en staging; candidata a archivo previa revisión | No |
| 68 | `fix/promover-fin04-fin05-a-fase1-20261106` | `20307ce3f30015d996e6c457b8cd87a09af3d396` | Contenida en staging; candidata a archivo previa revisión | No |
| 69 | `fix/promover-fin06-a-fase1-20261107` | `fc8f1ebaaad53accbc1897459143c4b39192e914` | Contenida en staging; candidata a archivo previa revisión | No |
| 70 | `fix/promover-fin07-a-fase1-20261107` | `249491241072b701fca030148ed9c64214b832fb` | Contenida en staging; candidata a archivo previa revisión | No |
| 71 | `fix/promover-sec06-roles-a-fase1-20261106` | `c3089866ba3928071336b79e0a2cd515c657712a` | Contenida en staging; candidata a archivo previa revisión | No |
| 72 | `fix/promover-smoke-migraciones-a-fase1-20261106` | `520a91f2804a80e7bf61b64e8e911fa677ee08b8` | Contenida en staging; candidata a archivo previa revisión | No |
| 73 | `fix/recepcion-po-persistencia-parcial-20261107` | `3cf69ac712194c9106f7d46712bc600dc99e5597` | Divergente; diferencias que evaluar: 2 rutas | No |
| 74 | `fix/reconciliar-staging-a-fase1-20261106` | `d80b0a121fa56f2b090400cd4621ca33bd340462` | Divergente en historia; mismo contenido de archivos que staging | No |
| 75 | `fix/sec02-aislamiento-lectura-20261106` | `d84bb5b4612869f96f258fa71776cbe3ec873528` | Contenida en staging; candidata a archivo previa revisión | No |
| 76 | `fix/sec03-http-tenant-regression-20261106` | `e9e0295974fc93c193078a08b4f9826cfa24dd9e` | Contenida en staging; candidata a archivo previa revisión | No |
| 77 | `fix/sec04-product-overposting-20261106` | `07b2c1671c27ceb68042927f6b78a7b7607369af` | Contenida en staging; candidata a archivo previa revisión | No |
| 78 | `fix/sec05-pagination-guard-20261106` | `3b45a0aabe02131e37b5cd8e66cc700d7b49f95f` | Contenida en staging; candidata a archivo previa revisión | No |
| 79 | `fix/sec06-roles-canonic-and-login-20261106` | `2bb3e061862b04ae9ebf6e5a75abcbae44b77c51` | Contenida en staging; candidata a archivo previa revisión | No |
| 80 | `fix/staging-b2b-bounded-query-20261106` | `ab967dd80464d18c4bf802a3a268d496a7eccfb9` | Contenida en staging; candidata a archivo previa revisión | No |
| 81 | `fix/staging-catalog-pagination-20261106` | `c2f01d3fd3203acf877abd173560127954d5d124` | Divergente; diferencias que evaluar: 2 rutas | No |
| 82 | `fix/staging-postgres-all-migrations-ci-20261106` | `f868654ef2a917e047689e7c2d080ccb245eb0f6` | Contenida en staging; candidata a archivo previa revisión | No |
| 83 | `fix/staging-proxy-https-hardening-20261106` | `64c00981bfe19e1d83a1939bde55844b57ed7e33` | Contenida en staging; candidata a archivo previa revisión | No |
| 84 | `fix/staging-sec02-integracion-20261106` | `28b2a78ba7b56e783290216d3b217ec0abc7d221` | Contenida en staging; candidata a archivo previa revisión | No |
| 85 | `grupo-a-integracion-fase3` | `91d1eda925d220a6ad581381851631c3c35b1c5f` | Contenida en staging; candidata a archivo previa revisión | No |
| 86 | `integracion/fase1-seguridad` | `345da7483e43f5f1b3058f35ce9d69fa614d159e` | Divergente en historia; mismo contenido de archivos que staging | No |
| 87 | `integracion/main-reconciliacion-20261102` | `48e63d987da96dac64f60afafbd9905dc8b222bd` | Render producción; autoDeploy ON | No |
| 88 | `integracion-auditoria-grupo-a` | `76cde3483a8c29c6501bd5c2163b1f4faf3bcd6f` | Contenida en staging; candidata a archivo previa revisión | No |
| 89 | `integracion-main-auditoria` | `ae6ad0821107dfc03185b6908ed00f923dab3906` | Divergente; diferencias que evaluar: 110 rutas | No |
| 90 | `main` | `e04c90696e59d86390216a8b51bf00e876cfebc5` | Vercel Production; PR #60 BASE | No |
| 91 | `staging/catalog-500-neon-test` | `2d1c37748fe645d75c16168dd1d3b6214285bcea` | Render staging; PR #60 HEAD y #64 BASE | No |
| 92 | `staging/vercel-render-neon-test` | `48d0f9ccd476c36ea119c571e344b59f7049318d` | Contenida en staging; candidata a archivo previa revisión | No |
| 93 | `test/fin01-postgres-real-20261106` | `5adcd2d57fde35fd9f6cef0f7848f92f09fed4da` | Contenida en staging; candidata a archivo previa revisión | No |

## Prohibiciones operativas hasta superar gates

1. No borrar ninguna rama en PR abierto ni conectada a Render/Vercel.
2. No borrar respaldos antes de verificar `git bundle` externo y un restore de ensayo independiente.
3. No suprimir historial divergente de `fix/auditoria` ni `integracion-main-auditoria`: el checkout múltiple y migraciones requieren evaluación funcional.
4. No ejecutar `git push --force`, `git branch -D`, `git push --delete` ni `git remote prune` antes de evaluar worktrees y dependencia externa.
5. Para descubrir worktrees en el equipo del desarrollador usar `scripts/consolidacion-preflight.ps1` **sin modificaciones**.

## Condiciones de promoción

Tests de CI del último SHA, PostgreSQL E2E y concurrencia real, copia verificable de base externa, protección efectiva de main, liberación separada de Vercel y Render y rollback probado. El PR #64 se mantendrá como borrador.
