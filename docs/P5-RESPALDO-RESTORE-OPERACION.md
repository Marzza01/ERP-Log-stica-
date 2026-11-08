# P5 — Recuperación independiente antes de cualquier despliegue

## Resultado de la inspección real

Neon proyecto `square-lake-18241058`: **0 snapshots** disponibles y **ninguna programación automática** de snapshots en la rama raíz. La base de evaluación `marketplaceerp_test` está en una rama secundaria `br-delicate-bar-bbjeww67` que no permite el snapshot del proveedor; el intento anterior devolvió `not allowed to snapshot non-root branch`. Un branch clone con 37/37 hashes iguales NO equivale a un backup lógico restaurable.

**Decisión P5.1: no permitir despliegue de SHA `d9e907c0ca7538350cc71d881c2e0f2b12be3153` hasta que se adjunte evidencia verificable de `pg_dump` y `pg_restore`.** No hay autorización para tocar Neon main ni migrar o sobrescribir bases preexistentes.

## Protocolo seguro — operador Windows
Ejecutar `scripts/p5-backup-restore-safe.ps1` en Windows con PostgreSQL **cliente 18 o superior** instalado y PowerShell. El script usa variables de entorno libpq, nunca muestra contraseñas, verifica base, protege de sobrescritura y comprueba las 37 tablas contra su huella de origen. **No subir dumps, secretos ni huellas que puedan revelar estructura sensible a GitHub o Vercel.**

### A. Respaldo de staging (solo lectura)

1. Confirmar visualmente en Neon que el endpoint origen corresponde a **`br-delicate-bar-bbjeww67`** y la base `marketplaceerp_test`. Crear o seleccionar un rol de mínimo privilegio que pueda leer tablas en `core`, `catalog` y `finance`. No usar Neon main ni la rama root.
2. Preparar variables **locales en la terminal** `P5_SOURCE_PGHOST`, `P5_SOURCE_PGUSER`, `P5_SOURCE_PGPASSWORD` y `P5_SOURCE_PGDATABASE=marketplaceerp_test`, opcional `P5_SOURCE_PGPORT`. Para la contraseña, emplear `Read-Host -AsSecureString` y convertirla exclusivamente a la variable de entorno local; no escribirla en archivos ni pasarla como argumento de CLI.
3. Elegir una carpeta privada fuera del repositorio y ejecutar:

```powershell
.\scripts\p5-backup-restore-safe.ps1 -Mode Backup -BackupFile "$HOME\Documents\P5-Backups\erp-staging-20261108.dump"
```

4. Conservar el `.dump`, `.fingerprints.txt` y la suma SHA256 **en almacenamiento privado cifrado** con acceso restringido. El volcado puede contener hashes de contraseña, datos de usuarios y transacciones, por tanto es confidencial.

### B. Restauración independiente (escritura solo en base vacía)
1. Crear **un proyecto Neon separado** de pruebas con el mismo mayor de PostgreSQL y una base **vacía** denominada `p5_restore_erp`. Usar cuenta/rol sin acceso a producción; host del destino debe ser **diferente** al origen.
2. Preparar `P5_TARGET_PGHOST`, `P5_TARGET_PGUSER`, `P5_TARGET_PGPASSWORD`, `P5_TARGET_PGDATABASE=p5_restore_erp`; conservar las variables de origen solo para comparar identidades de host/base.
3. Comprobar que el destino está vacío antes de ejecutar:

```powershell
.\scripts\p5-backup-restore-safe.ps1 -Mode Restore -BackupFile "$HOME\Documents\P5-Backups\erp-staging-20261108.dump"
```

4. El script se detiene si el host es igual al origen, la base no tiene prefijo `p5_restore_`, ya existen tablas o falla `pg_restore`. La aceptación exige `RESTORE_VERIFICADO=37_tablas_con_huellas_iguales` y SHA256 del archivo, sin publicar el archivo de datos.
5. Registrar que se restauraron 3 historiales de migraciones EF, 500 productos y las demás tablas con exactamente las mismas huellas. Debe adjuntarse evidencia de los comandos redaccionada y resultado de API de solo lectura contra el destino; **no** simular este resultado si el script no se ejecutó.

### C. Condiciones para despliegue de staging
- Backup recuperable verificado, revisión de variables y proveedor de pagos **Disabled**, credenciales de prueba con mínimos privilegios.
- Render staging `srv-dbmnd6jrjlhs73c0jung` en `staging/catalog-500-neon-test`, `autoDeploy=no`. Último deploy live observado en `47af11a80a72fc26f585caa47da4dbe584463b6e`, **no** en SHA nuevo.
- Desplegar el SHA **exacto** `d9e907c0ca7538350cc71d881c2e0f2b12be3153` solo después del backup, y conservar ID/URL de deploy anterior para rollback de código.
- E2E autenticado en staging Preview+Render contra API real, lecturas de productos/órdenes/contabilidad, roles y respuestas 401/403; sin pagos ni mutaciones irreversibles. Documentar SHA del frontend y del backend (pueden diferir).
- Revisar logs y métricas en ventanas comparables; evitar afirmar error-free si retención o proveedor no las entrega.

## Bloqueos restantes (orden 3–7)
- **3:** CI verde para staging SHA final, pero E2E de esa versión desplegada aún pendiente.
- **4:** Render `error,fatal` sin entradas en la consulta disponible y Vercel sin runtime errors 24 h; no hay latencia HTTP medible en Render para la ventana consultada. No inferir SLO.
- **5:** Issue #51, **9 avisos dev** (2 high/7 moderate), `npm audit --omit=dev` **0**. Registrar remediación con lockfile y test; no forzar upgrades.
- **6:** PR #2 divergente de staging `56 ahead / 10 behind`; `main...staging` `174 ahead / 0 behind` y migraciones Catalog/Finance. No fusionar `main` sin auditoría de schema destino.
- **7:** Issue #57 no se cierra hasta obtener prueba de recuperación, despliegue, E2E real y aprobación explícita de promoción.

## Riesgos
El script de respaldo se aporta como **procedimiento, no ejecución verificada**. Revisar el script antes de usarlo con datos reales; no contiene credenciales. En ausencia del backup independiente, mantener las condiciones de no despliegue.
