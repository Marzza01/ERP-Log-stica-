# Configuración segura de LOGIMKT ERP

U1 retira los secretos literales de la configuración versionada. No cambia inventario,
pagos, checkout, esquema ni migraciones, y no habilita el arranque de servicios.

## API .NET: variables del proceso

`WebApplication.CreateBuilder` ya carga los proveedores de configuración estándar.
Las variables de entorno con doble guion bajo representan claves jerárquicas:

| Variable | Clave | Requisito |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | `ConnectionStrings:DefaultConnection` | Conexión completa a un destino expresamente autorizado; obtenerla de una fuente privada. |
| `Jwt__Key` | `Jwt:Key` | Clave aleatoria, privada y de alta entropía; la validación actual exige al menos 32 caracteres. |
| `ASPNETCORE_ENVIRONMENT` | Entorno del host | Elegir explícitamente; no usar Development para eludir controles productivos. |
| `Jwt__Issuer` | `Jwt:Issuer` | Opcional; conservar el emisor acordado con los consumidores. |
| `Jwt__Audience` | `Jwt:Audience` | Opcional; conservar la audiencia acordada. |
| `Jwt__ExpirationMinutes` | `Jwt:ExpirationMinutes` | Opcional; conservar una duración limitada. |
| `Cors__AllowedOrigins__0` | `Cors:AllowedOrigins:0` | Origen autorizado; índices adicionales para otros orígenes. |

No se incluyen claves ni contraseñas reales de ejemplo. Inyectar las variables en el
proceso desde un gestor de secretos o un mecanismo privado de desarrollo. No guardar
valores efectivos en scripts versionados, parámetros de comandos, archivos compartidos
ni capturas de pantalla. No imprimir el entorno ni la configuración resuelta en logs.

La API .NET **no carga automáticamente `.env` ni `.env.example`**. Copiar la plantilla no
configura un `dotnet run`. Este proyecto no incorpora un `UserSecretsId`; esta entrega
usa los proveedores de variables de entorno existentes, sin inicializar secretos de
usuario ni modificar configuración global.

En los proveedores estándar, el JSON del entorno sobrescribe el JSON base, las
variables de entorno sobrescriben el JSON y los argumentos de configuración tienen
prioridad sobre esos proveedores. Existen dos excepciones heredadas en `Program.cs`:

1. Si `DATABASE_URL` no está vacía, su traducción reemplaza la conexión configurada.
2. Si `CONNECTION_STRING` no está vacía, reemplaza después la conexión, incluso la
   procedente de `DATABASE_URL` o de los proveedores estándar.

No definir fuentes de conexión contradictorias. Preferir la clave canónica
`ConnectionStrings__DefaultConnection` y dejar los dos alias sin definir cuando no se
necesiten. Se conserva el parser existente de `DATABASE_URL`; U1 no certifica nuevos
formatos ni cambia su tratamiento de TLS. `JWT_KEY`, `JWT_ISSUER`, `JWT_AUDIENCE` y
`CORS_ALLOWED_ORIGINS` no son por sí solos claves jerárquicas consumidas por la API.

`appsettings.json` y `appsettings.Development.json` dejan vacías la conexión y la clave
JWT. Se conservan emisor, audiencia, expiraciones, CORS y logging de main. Sin una
clave JWT válida la validación existente rechaza el arranque antes de inicializar la
base; no se genera una clave temporal ni se usa una contraseña predeterminada.

## Docker Compose

Compose obtiene sus sustituciones del entorno del comando o de su archivo `.env`.
Los valores efectivos de ese archivo deben mantenerse privados y fuera de Git.

- `POSTGRES_PASSWORD` es obligatoria para el contenedor de base.
- `CONNECTION_STRING` es obligatoria para la conexión de la API; Compose la transmite
  como `ConnectionStrings__DefaultConnection`. Proporcionar una cadena completa, con
  valores correctamente escapados para Npgsql y coherentes con la base configurada.
  La contraseña no se concatena mediante interpolación dentro de una cadena fija.
  Para este Compose, configurar exclusivamente `Host=db;Port=5432`; la base/usuario
  conservan sus nombres de main. `localhost` dentro de `app` no designa a `db`.
  No usar `host.docker.internal`, direcciones del host ni los destinos de las
  instalaciones PostgreSQL 18 o del laboratorio PostgreSQL 15. La cadena completa
  es un dato externo: Compose no valida su semántica Npgsql; el operador debe revisar
  el destino en privado antes de cualquier futura autorización de arranque.
- `JWT_KEY` es obligatoria y Compose la transmite como `Jwt__Key` a la API.
- Las tres usan `${VARIABLE:?mensaje}`: una variable ausente o vacía provoca rechazo
  de la interpolación en lugar de aportar un secreto predeterminado.
- Las restantes variables de la plantilla no se transmiten automáticamente a los
  contenedores: solo se aplican las declaradas por el Compose correspondiente.

U1b conserva servicios, imágenes, red, volúmenes y logging. Elimina únicamente la
publicación de puertos del servicio `db`; no elimina ni recrea `postgres_data`.
La cadena externa debe definir los parámetros de conexión/TLS adecuados; no se fuerza
`Include Error Detail=true` desde Compose.

Los servicios comparten la red predeterminada de Compose. `app` depende de `db` y el
frontend depende de `app`. El Dockerfile del frontend copia
`frontend/marketplace-ui/nginx.conf`, cuyo proxy conserva `/api/` hacia `http://app:8080`
sin eliminar el prefijo; la variante `nginx.conf` de la raíz apunta al mismo servicio.
No se requieren cambios en nginx ni en los Dockerfiles para U1b.

**PostgreSQL no publica ningún puerto en el host.** Su puerto interno 5432 no implica
un enlace al puerto 5432 de Windows. No existe un valor predeterminado de publicación
5432 ni 55432, y `POSTGRES_HOST_PORT` ya no es consumida. API conserva `8080:8080` y
frontend conserva `3000:80`; su exposición, TLS y endurecimiento siguen pendientes
de revisión. La red predeterminada compartida no constituye aislamiento de egreso.

No se entrega un override de acceso externo porque no existe una necesidad demostrada.
Si fuera necesario: documentar el motivo y solicitar autorización; preparar para
revisión un archivo separado que no se cargue automáticamente; exigir un puerto
elegido y verificado por el operador, sin valor predeterminado, enlazado exclusivamente
a `127.0.0.1`; revisar su configuración combinada y autorizar separadamente el arranque.
No asumir que 5432 o 55432 están disponibles. La comprobación de disponibilidad no
reserva el puerto. Esta entrega no autoriza iniciar Compose ni acceder o modificar
PostgreSQL 18 o el laboratorio PostgreSQL 15.

Las variables de Compose se convierten en entorno de contenedor. Sus valores pueden
ser visibles para administradores y mediante herramientas de inspección; no son un
almacén de secretos cifrado. No publicar salidas de `docker compose config` con valores
resueltos. No cambie la contraseña de una instalación existente suponiendo que una
variable actualizará automáticamente las credenciales persistidas.

## Límites de esta configuración

`ADMIN_EMAIL` y `ADMIN_PASSWORD` se mantienen externos y vacíos en la plantilla. No se
invoca bootstrap. La configuración de pasarela de main conserva sus rechazos actuales;
añadir conexión y JWT no certifica el arranque productivo ni habilita pagos reales.

U4 separa `StartupInitializer` del camino HTTP. Migraciones, bootstrap Admin y seed
demo requieren comandos CLI explícitos y confirmación del destino; consulte
[Arranque y mantenimiento U4](Arranque_Mantenimiento_U4.md). Configurar secretos o
`Seed:DemoData` no los activa. La operación real de esos comandos sigue pendiente
de auditoría y autorización; ninguna validación de esta entrega arranca servicios.
Las comprobaciones originales U1 **no ejecutaron el host, endpoints de salud, Docker,
EF ni conexiones PostgreSQL**. Production sigue sujeto al bloqueo de pagos de main.

Las pruebas U1 cargan JSON y el proveedor de entorno de .NET con valores sintéticos
aislados, sin instanciar la API ni un proveedor de base. La revisión de Compose es
estática: no equivale a iniciar ni validar un despliegue.

Retirar un secreto del árbol actual no lo elimina del historial Git ni de copias
anteriores. Si alguno de los valores previos fue real o reutilizado, gestionar su
rotación/revocación por separado. U1 no cambia credenciales de bases ni reescribe Git.
