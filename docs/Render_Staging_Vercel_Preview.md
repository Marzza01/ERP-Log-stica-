# Ensayo aislado: Render staging, Neon test y Vercel Preview

Este documento prepara operaciones futuras; no autoriza crear servicios, desplegar,
ejecutar migraciones ni crear el primer Admin. Neon `main`, la rama de respaldo,
Render Production y Vercel Production quedan fuera del procedimiento.

## Identidad y precondiciones

- Proyecto Neon: `square-lake-18241058` (`marketplaceerp-test`).
- Rama exclusiva: `br-delicate-bar-bbjeww67` (`test-migraciones-erp-20261104`).
- Endpoint Direct: `ep-jolly-breeze-bbw4v48n.c-15.us-east-1.aws.neon.tech`.
- Base: `marketplaceerp_test`; rol: `marketplaceerp_test_owner`.
- Cotejar nuevamente endpoint y branch ID en Neon antes de crear el servicio.
- Revisar en esa rama los tres historiales EF y `core.usuarios` en solo lectura.
- Confirmar que la cuenta Render permite otro servicio Free y aceptar sus límites.

## Servicio Render separado

Tras autorización de creación y despliegue, en Render: New > Web Service,
repositorio `Marzza01/ERP-Log-stica-`, rama
`staging/vercel-render-neon-test` con el commit revisado de esta fase,
nombre exacto `marketplaceerp-api-staging`, runtime Docker, root/build context
raíz del repositorio y Dockerfile `src/MarketplaceERP.API/Dockerfile`.
Desactivar Auto-Deploy inicialmente. No usar un comando Docker personalizado:
el ENTRYPOINT inicia HTTP sin migraciones ni bootstrap. Configurar el health
check `/health/ready` y `PORT=8080`; la API escucha en `0.0.0.0`.

Variables del **servicio nuevo**, introducidas solo en el gestor de Render:

| Nombre | Condición |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `Deployment__Target` | `NeonTest` |
| `Payments__Provider` | `Disabled` |
| `CONNECTION_STRING` | Cadena Npgsql privada para el endpoint Direct de ensayo, base y rol anteriores; `SSL Mode=VerifyFull`, `Channel Binding=Require` y contraseña del rol obtenida en Neon. |
| `Jwt__Key` | Clave privada propia de ensayo, distinta de Production. |
| `PORT` | `8080` |

No definir `DATABASE_URL` ni `ConnectionStrings__DefaultConnection` en este
servicio. No copiar grupos de variables de Production. `Jwt__Issuer` y
`Jwt__Audience` pueden conservar los valores versionados si todos los clientes
de ensayo los usan; no introducir claves nuevas en el repositorio. Para el
proxy del mismo origen, CORS puede permanecer sin orígenes permitidos; si se
prueban llamadas directas desde el navegador, autorizar exclusivamente la URL
Preview concreta, nunca `*`.

La API rechaza el arranque si el nombre Render y `Deployment__Target` no
coinciden, si existen fuentes de conexión contradictorias o si host, puerto,
base, rol, `VerifyFull` y channel binding no corresponden a ensayo. Esa guarda
valida la configuración antes de abrir EF. No demuestra por sí sola la
propiedad del endpoint: se coteja con los metadatos de Neon.

Después del despliegue autorizado: comprobar que Render muestra el commit
esperado; `GET /health` = 200; `GET /health/ready` = 200; verificar en Neon,
mediante consultas de solo lectura, que no hubo migraciones nuevas. Un 200 de
`/health/ready` confirma conectividad, no los historiales ni la identidad de
la rama.

## Vercel Preview

El middleware `/api/:path*` del frontend selecciona destinos fijos por
`VERCEL_ENV`. Production usa la API Production actual. Preview **solo** acepta
`API_STAGING_ORIGIN=https://marketplaceerp-api-staging.onrender.com`, definida
en Vercel para Preview y, preferiblemente, limitada a la rama de ensayo. Si
Render asigna otra URL, revisar código y pruebas antes de cambiarla; el proxy
no acepta una URL arbitraria. Sin esa variable o con un valor distinto,
Preview devuelve 503 sin llamar a Production. Desarrollo local conserva el
proxy de Vite hacia `localhost:5000`.

Publicar una Preview requiere autorización de commit/push o despliegue CLI.
No usar `--prod` ni promover la Preview. Revisar la protección de acceso de
Vercel. Probar `/login`, un 401 sin sesión, login con Admin de ensayo, recarga,
expiración y logout. Comprobar con las herramientas del navegador que `/api`
responde desde el origen Preview y que las llamadas alcanzan únicamente el
servicio Render staging. No probar pagos ni liquidaciones reales.

Actualmente la autenticación usa JWT Bearer en `localStorage`, no cookies.
El proxy no reenvía cookies del navegador ni `Set-Cookie` del backend para
evitar filtrar cookies de Vercel. La adopción de cookies HttpOnly requiere una
fase separada con CSRF y revisión de dominios. El proxy conserva método,
query, cuerpo, Authorization y estado HTTP; Preview debe verificar ese
comportamiento en vivo. El middleware tiene límites de tamaño y duración de
Vercel que deben medirse antes de usar cargas grandes o importaciones.

## Primer Admin de ensayo, solo tras autorización separada

La rama test tiene `core.usuarios` y sus migraciones, pero ninguna cuenta.
Render Free no ofrece shell/one-off jobs. Ejecutar desde un proceso local de
mantenimiento confiable, contra el endpoint Direct de ensayo, usando
`--maintenance=bootstrap-admin` con host, puerto, base y huella exactos.
Verificar los metadatos Neon y el fingerprint inmediatamente antes. Introducir
`ADMIN_EMAIL`, `ADMIN_PASSWORD` y la conexión de forma protegida, solo en el
proceso local; no pasarlos como argumentos ni guardarlos en scripts, logs o
archivos. El bootstrap existente valida un Admin activo y la contraseña si
la cuenta ya existe; no la sobrescribe. No ejecutar en paralelo. Borrar las
variables transitorias al terminar. Confirmar después un único Admin activo
y el login de ensayo. Preparar y revisar el lanzador seguro de bootstrap antes
de conceder la autorización de ejecución.

## Recuperación

Como Production no se modifica, un fallo de Preview se recupera dejando de
usar esa URL o redeplegando la última Preview sana. Un fallo de staging se
atiende en ese servicio y en la rama Neon test; nunca restaurar `main` ni el
respaldo para reparar ensayo. Detener cualquier operación cuyo destino no
coincida con los identificadores anteriores. Suspender o eliminar recursos de
ensayo requiere autorización específica.
