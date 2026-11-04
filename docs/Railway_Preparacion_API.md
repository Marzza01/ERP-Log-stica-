# Preparación de API para pruebas en Railway

Usar la raíz del repositorio como contexto de compilación y seleccionar
`src/MarketplaceERP.API/Dockerfile` con `RAILWAY_DOCKERFILE_PATH` en el servicio.
La configuración versionada `railway.json` se retiró porque el mecanismo Config as
Code está obsoleto; configurar el healthcheck `/health` y el comando de inicio
desde el servicio. El `ENTRYPOINT` del Dockerfile inicia la API, sin migraciones.
`PORT` lo proporciona Railway en tiempo de ejecución; la API escucha en
`0.0.0.0:PORT`.

Variables de la API: `ASPNETCORE_ENVIRONMENT=Production`, `Payments__Provider=Disabled`,
`Jwt__Key`, `Cors__AllowedOrigins__0` y una sola fuente de conexión privada:
`DATABASE_URL`, `CONNECTION_STRING` o `ConnectionStrings__DefaultConnection`.
Las variables JWT de emisor, audiencia y expiración pueden configurarse si los
clientes requieren valores diferentes. No usar `DATABASE_PUBLIC_URL`; mantener
PostgreSQL sin acceso público. No configurar `Payments__EnableMock=true`.

`/health` comprueba que el proceso responde; `/health/ready` comprueba conexión,
pero no demuestra que los tres esquemas estén migrados. El arranque HTTP nunca
aplica migraciones ni crea Admin. Los comandos de mantenimiento siguen requiriendo
invocación explícita y confirmación del destino. No ejecutarlos automáticamente
al desplegar; auditar primero las migraciones divergentes de Core, Catalog y Finance.

Con pagos deshabilitados, checkout, generación/procesamiento de liquidaciones,
conciliación, reembolso, transiciones de órdenes, asiento manual y match de compras
se rechazan con HTTP 503 antes de entrar a sus acciones. Las consultas de lectura siguen sujetas a sus roles.
El modo `Mock` solo se admite en Development y no representa dinero real.
