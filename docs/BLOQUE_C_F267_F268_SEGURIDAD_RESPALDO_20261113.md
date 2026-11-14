# Bloque C — F267 roles y F268 respaldo (13-nov-2026)

## Restricciones
Solo STAGING y PostgreSQL efímero localhost. Sin pagos reales, sin alterar producción, sin fusionar main, sin credenciales en archivos.

## F267 — Regresiones y autenticación
- HTTP TestServer con JWT sintéticos sobre PostgreSQL local: POST checkout V2 anónimo 401, Admin 403, Vendedor 403, Comprador con pasarela Disabled 503 (PagosDeshabilitados). Sin órdenes, intentos, enlaces 1:N ni reservas creadas; sin llamadas a pasarela.
- Tests CheckoutMultiSkuHttpTests, UsuariosRolesCanonicosTests y DeploymentHttpSecurityTests: **36/36 PASS, exit 0**.
- Script scripts/f267-staging-roles-acceptance.mjs ejecutado sin credenciales: /health=200, /health/ready=200, cola Admin anónima=401, POST checkout anónimo=401, exit 0; no escribe. Los resultados anónimos NO equivalen a pruebas por rol.
- scripts/f267-staging-roles-secure.ps1 -Autenticado permite que el operador introduzca en Windows las claves legítimas de Admin, Comprador y Vendedor. El script nunca imprime claves ni JWT. El POST de checkout utiliza carrito vacío, que no permite ningún pago aunque la configuración estuviera accidentalmente habilitada. Exige 503 PagosDeshabilitados, acceso Admin y 403 para roles ajenos.
- **Pendiente de ejecutar** con contraseñas legítimas en STAGING. No se extraen ni adivinan claves.

## F268 — Respaldo y rollback
- Neon STAGING: proyecto tiny-sound-11266101, rama br-aged-morning-bbordmzw, endpoint Direct ep-sparkling-smoke-bbcm2qma.c-15.us-east-1.aws.neon.tech, DB marketplaceerp_test.
- Snapshot F171 snap-dry-dream-bbi2f5px del 12-nov **pre-migraciones**, no equivale a backup actual. El intento de crear un snapshot nuevo devolvió HTTP 422 snapshots limit exceeded; no se borró el anterior.
- Rollback de código Render STAGING: deploy anterior dep-dbrr0e6i0phs73b8d9lg (5922003) y deploy operativo dep-dbrre056e07s738086u0 (7161e3a). No se ejecutó rollback.
- Alternativa segura: scripts/f268-backup-neon-staging-local-restore.ps1. En -SoloPreflight verificó parseo con 0 errores y herramientas/archivos disponibles. Con -Ejecutar pide al operador credenciales Neon STAGING ocultas, comprueba host/base/rol exactos y ejecuta backup custom P5 con SHA256, huellas históricas de 37 tablas y huellas adicionales de finance.cancelaciones_pre_envio y finance.intento_reservas_pago fuera del repositorio; exige un total de 39 tablas e historiales Core2/Catalog7/Finance10 en origen/restauración. Comprueba archivo con pg_restore --list y restaura a una base local recién creada con prefijo ci_f268_restore_ y UUID solo si TEST_PG_CONNECTION es localhost:5432/postgres. Compara huellas y limpia exclusivamente esa base temporal. No escribe en Neon.
- **Pendiente de ejecutar** backup/restauración actuales porque no hay claves Neon en sesión. Tampoco se certificó captura y reversión efectiva de configuración Render/Vercel.

## No-Go para cierre de C
Permanecen abiertos: login real de los 3 roles, 503 Comprador en STAGING, E2E de Vercel Preview y 15 módulos por rol, recorridos B2B/B2C reales simulados, recuperación de pagos inciertos sin reenvío y backup/rollback actual verificado. Los tests de CI no equivalen a aceptación de despliegue. No marcar los 9 controles técnicos de Issue #65 sin sus evidencias. PR #60 sigue Draft.
