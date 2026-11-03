# U4 — HTTP sin mantenimiento implícito

Base: `c87a44c9911477be332981e647d9445cd8822ea7` en la rama de reconciliación.
Este documento describe código preparado, no una autorización para ejecutar la API,
Docker, migraciones, bootstrap ni seed. No se conectó a ninguna base.

## Inventario de efectos y decisión

| Ubicación | Comportamiento anterior | Resultado U4 |
|---|---|---|
| Program → StartupInitializer.InicializarAsync | Siempre migraba, intentaba bootstrap y seed antes de HTTP | Llamada eliminada; HTTP no resuelve los contextos para inicialización. |
| Migraciones del inicializador | Core → Catalog → Finance, conexión independiente y advisory lock; podía ocultar fallos en Development | Solo CLI migrate; se recupera la implementación explícita de integración, plazo de lock acotado y fallo propagado. |
| Seed Admin | Dependía del entorno y ADMIN_EMAIL/ADMIN_PASSWORD; podía crear una cuenta al arrancar | Solo CLI bootstrap-admin. Credenciales externas obligatorias y validación de cuenta existente; no promoción/reparación implícita. |
| Seed demo | Predeterminado activo en Development; creaba vendedor/categoría e importaba CSV con datos WMS | Solo CLI seed-demo en Development. No es requisito para servir una base ya preparada. Sin la acción explícita no se importa ni modifica nada. |
| AddCore/Catalog/FinanceServices | Registran contextos y servicios scoped; Finance valida configuración de pagos | Registros conservados; ninguna tarea hosted de mantenimiento encontrada. Finance sigue bloqueando Production. |
| ReservasExpiracionService, recuperación, liquidaciones, stock | Servicios invocados por solicitudes; no tareas de arranque registradas | Intactos. No se añade conciliación/reparación/expiración automática. |
| ProductosController seed-500 | Importación explícita por endpoint privado Admin | Se conserva U2. No es un endpoint público ni se llama al arrancar. No se certifica su idoneidad para bootstrap. |
| Health /health y /health/ready | El primero es liveness; ready consultaría conexiones bajo solicitud | Intactos y anónimos; no ejecutan migraciones ni se invocaron en pruebas. Ready puede abrir conexiones al recibir una solicitud. |
| Dockerfile API y Railway | Ejecutan dotnet MarketplaceERP.API.dll sin modo de mantenimiento | Intactos: seleccionan HTTP. Restore/publish del Dockerfile son compilación, no una orden de migración. |
| Compose y Dockerfile nginx | Servicios normales, healthcheck pg_isready del contenedor DB y proxy | Sin cambios. PostgreSQL sigue sin publicar puertos al host. La imagen PostgreSQL inicializaría su volumen si se iniciara; U4 no arranca ni controla esa inicialización propia de la imagen. |
| INICIAR_LOCAL.bat | Invoca dotnet run para HTTP | Intacto; ya no provoca el inicializador de base por esa vía. No se ejecutó. |
| CI y scripts de validación | Restore/build/test; CI puede disponer una base desechable; script de fase 8 consulta migraciones con dotnet ef | No arrancan mantenimiento por flags nuevos. No se ejecutaron CI, dotnet test general ni dotnet ef. La suite completa queda fuera de estas pruebas locales. |

Se buscaron Migrate/EnsureCreated/SQL de mantenimiento, callbacks de host, timers,
Task.Run e implementaciones/registros hosted en src, además de entrypoints y workflows.
Las migraciones modificadoras quedan en StartupInitializer, alcanzables por el runner
CLI. No se encontraron EnsureCreated en el camino productivo ni comandos Exec de
mantenimiento en los csproj. Generadores de CSV independientes no son parte del arranque.

## Separación y comandos

Program analiza exclusivamente los argumentos de proceso antes de WebApplication.CreateBuilder.
Sin comando de mantenimiento, la selección es HTTP. Un comando reservado incompleto,
mal escrito o combinado con argumentos HTTP se rechaza con código 2; no cae a HTTP.
El componente de selección no lee configuración ni entorno para activar mantenimiento.
El camino de mantenimiento retorna antes de crear la aplicación web, tanto si termina
con éxito como si devuelve error. No construye ni inicia un host; usa configuración y
un proveedor DI limitado a contextos e importador, sin registrar pasarela de pagos.

Gramática futura, **NO AUTORIZADA PARA EJECUCIÓN**:

```text
dotnet MarketplaceERP.API.dll --describe-maintenance-target
dotnet MarketplaceERP.API.dll --maintenance=<acción> --expected-host=<host> --expected-port=<puerto> --expected-database=<base> --confirm-target=<SHA256>
```

Acciones admitidas: `migrate`, `bootstrap-admin`, `seed-demo`. Orden y número exactos
de argumentos son obligatorios. No se admiten acciones combinadas, --migrate antiguo,
contraseñas como argumentos, diagnóstico WMS ni comandos de U5.

La descripción solo interpreta configuración con NpgsqlConnectionStringBuilder,
sin contextos, conexiones ni SQL. Su huella vincula host, puerto, base y usuario, con
codificación no ambigua; no contiene ni deriva de la contraseña. La confirmación exige
host/puerto/base idénticos y huella SHA-256 coincidente antes de abrir conexiones.
No certifica versión PostgreSQL, directorio de datos, identidad física ni servidor
tras DNS/proxy; esos controles requieren una futura auditoría del entorno.

Configuración CLI: appsettings copiados al directorio del ensamblado y proveedores
estándar de entorno del builder genérico. Usar ConnectionStrings__DefaultConnection
o CONNECTION_STRING (este alias prevalece si está definido). DATABASE_URL se rechaza
para evitar una traducción diferente/silenciosa del destino. HTTP conserva todos sus
proveedores y aliases existentes. Las credenciales no se imprimen; solo descripción
de destino y huella. El contenido de argumentos no se imprime cuando el parser falla.

Antes de una acción modificadora, el runner comprueba current_database() en los tres
contextos configurados al mismo destino. Después ejecuta solo la acción seleccionada.
El modo bootstrap no migra; migrate no crea Admin ni importa catálogo; seed-demo no
migra ni crea Admin. Seed demo exige Development además de la invocación deliberada;
Development por sí solo no inicia nada. Seed:DemoData y flags Startup/Maintenance no
activan el camino CLI ni el seed en HTTP.

Bootstrap valida correo/contraseña externos y una cuenta existente debe coincidir
con Admin activo y hash válido; de lo contrario rechaza sin promoverla. El seed demo
conserva la creación de vendedor/categoría e importación existentes; ahora propaga
errores y ausencia de CSV. Puede producir cambios parciales. No se presentó como
requisito de dependencia normal: una instalación debe aprovisionarse explícitamente.

Código 0 indica finalización del comando; 1 indica excepción/fallo del runner; 2 error
de argumentos. No se imprimen excepciones completas del proveedor en el runner.
Un fallo/interrupción no demuestra rollback ni autoriza reintento automático.

## Límites operativos antes de cualquier ejecución

- Migrate aplica todas las migraciones pendientes de Core, Catalog y Finance en ese
  orden; no es el SQL limitado de InitialCreate ni sustituye la auditoría del laboratorio.
  No ejecutar estos comandos sobre PostgreSQL 18, el laboratorio PostgreSQL 15 ni
  ningún destino real sin autorización independiente. No asumir puertos disponibles.
- No hay atomicidad entre los tres contextos, bootstrap y seed; una interrupción puede
  dejar aplicación parcial. Se requieren respaldo, plan de recuperación y revisión
  de historial EF/datos antes de repetir. El advisory lock coordina migraciones
  compatibles, no todos los escritores ni los seeds. Los seeds no tienen exclusión
  global de concurrencia ni transacción entre módulos.
- La huella es de configuración, no una prueba de identidad física. El runner no
  verifica versión 15.19 ni usa la auditoría SQL attempt7. No se afirma que las
  migraciones o el mantenimiento estén aprobados para ejecución.
- Las excepciones de servicios importadores/EF podrían registrar detalles en sus
  proveedores de logging existentes; revisar permisos, destino y retención de logs
  antes de ejecutar mantenimiento con credenciales reales. No volcar configuración.
- HTTP ya no requiere permisos DDL ni ADMIN_EMAIL/ADMIN_PASSWORD para inicializarse.
  Una base sin esquema o datos necesarios fallará en las operaciones que los requieran;
  HTTP no la reparará. Usar credenciales de ejecución con privilegios mínimos.
- Se preserva el bloqueo de Production de Finance: U4 no certifica un arranque
  productivo completo ni habilita pagos reales. JWT y las 80 restricciones Admin
  permanecen sin cambios, al igual que checkout/propiedad de comprador de U3.

## Validación local

La API compiló con --no-restore: cero errores; primera compilación con nueve avisos
CS0618 preexistentes en Catalog, compilación final incremental sin advertencias.
Se ejecutaron 38 casos aislados U2/U3 y 19 de U4 con fuentes Fact/Theory y referencias
locales mediante Roslyn/runner aislado. No es ejecución del runner xUnit completo ni
HTTP real. El compilador manual avisa CS1701 por ensamblados xUnit en caché.

U4 prueba selección HTTP sin llamar al delegado de mantenimiento, selección explícita
y exclusión de HTTP, propagación de fallo simulado sin caída a HTTP, rechazo de
argumentos ambiguos/mezclados, registros de módulos sin tareas hosted y conservación
del rechazo productivo de pagos, huellas y confirmaciones offline. Una prueba estática
adicional verifica el enlace de Program y ausencia del inicializador en HTTP.
La primera ejecución detectó una variante inválida de describe que no se rechazaba;
se corrigió el parser antes de cerrar la validación.

No se ejecutaron MaintenanceRunner ni migraciones/bootstrap/seed reales, no se abrió
la API real ni PostgreSQL. Los efectos de arranque se verifican por inspección de código,
componentes aislados y contrato estático; no se presentan como prueba de despliegue.
Las pruebas InMemory de U3 usan exclusivamente datos en RAM. La suite general y pruebas
PostgreSQL permanecen omitidas. No se instalaron ni restauraron dependencias.
