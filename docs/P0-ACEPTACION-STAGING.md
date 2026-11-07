# P0 · Aceptación funcional contra Render + Neon staging

**Entorno permitido:** `https://marketplaceerp-api-staging.onrender.com` (Render staging) y la rama `test-migraciones-erp-20261104` de Neon.

**Prohibido:** producción, GitHub `main`, Neon `main`, transferencias bancarias, liquidaciones reales, correos a proveedores, operaciones de terceros.

## Comprobaciones automáticas realizadas por GitHub Actions

- Integración con PostgreSQL efímero: PO, recepción parcial, recuperación, idempotencia, factura, conciliación y asiento.
- Dos peticiones HTTP concurrentes con JWT sobre la misma PO.
- Rollback de match si falla la generación contable.
- Protección HTTP Admin/401/403, bloqueos de pagos `503` y controles anti-duplicados.
- Suite Core, Catalog, Finance y frontend aprobada.

Esto **no** demuestra todavía el recorrido autenticado sobre el Render + Neon compartido de evaluación.

## Ejecutar el paso pendiente sin entregar credenciales a ChatGPT

1. En un equipo confiable, abrir la carpeta del repositorio en `staging/catalog-500-neon-test` (o una rama que contenga este script).
2. Tener Node.js 20 o superior. Verificar con `node --version`.
3. Primero ejecutar **solo lectura**:

   ```bash
   node scripts/p0-staging-acceptance.mjs --preflight
   ```

4. Abrir una terminal **local** del operador y disponer de credenciales **legítimas de Admin de staging** a través de variables `P0_ADMIN_EMAIL` y `P0_ADMIN_PASSWORD`; alternativamente, establecer `P0_ADMIN_TOKEN` con un JWT de Admin previamente obtenido de forma legítima. Nunca introducirlas en este chat, en el repositorio, en GitHub Actions, en logs ni en archivos de texto.
5. Si se acepta generar dos PO sintéticas (que quedarán para auditar) y datos de stock en **staging solamente**, establecer `P0_EXECUTE_STAGING=CONFIRMO` en la terminal local y ejecutar:

   ```bash
   node scripts/p0-staging-acceptance.mjs --execute
   ```

6. El script comprueba los tres bloqueos HTTP `503` para Match financiero y Liquidaciones **antes** de crear PO alguna. Si cualquiera no está bloqueado, aborta.
7. Al finalizar, copiar únicamente la línea `P0_EVIDENCE=...`, que no incluye credenciales, al registro de aceptación. Retirar las variables locales de credenciales.

## Qué realiza el modo --execute

- Login real de Admin y acceso a Compras.
- Primera PO sintética: dos SKUs del catálogo demo; envía *solo el cambio de estado interno*.
- Falla intencionalmente la segunda línea por una ubicación WMS inexistente; confirma que la primera persiste.
- Reintenta sin duplicar stock, comprueba cantidades, replays válidos y token alterado.
- Registra factura sintética, verifica rechazo de duplicado. No ordena un pago.
- Segunda PO de 5 unidades: realiza dos recepciones HTTP en paralelo, exige un único éxito y un `409`, sin exceso.
- Genera evidencia `P0_EVIDENCE` con PO creadas, estado por etapa y error si alguno falla.

## Validación posterior obligatoria

Consultar la rama **Neon staging** por los ID de PO en `P0_EVIDENCE`, no mediante modificaciones manuales. Confirmar que:

- El acumulado recibido por Finance coincide con la suma de los movimientos inbound por PO.
- La primera PO tiene **5 unidades** recibidas y **dos** movimientos; la segunda tiene **5 unidades** y **un** movimiento.
- No existen facturas duplicadas para la PO sintética.
- No existen pagos, liquidaciones ni asientos de prueba no autorizados.
- La PO original `PO-DEMO-EVAL-20261107-001` continúa en Borrador.
- Los registros de Render carecen de excepciones inesperadas y la API sigue `ready`.

Si el script falla en cualquier paso, **no rerun a ciegas**. Cada nueva ejecución genera nuevas PO; consultar las PO indicadas en la evidencia antes de repetir.

## Criterio de cierre

P0 en entorno desplegado es **APROBADO** solo cuando:
1. `--execute` termina con `completed: true`.
2. Los conteos de Finance y Catalog en Neon coinciden con la evidencia.
3. Se ratifica que Match y pagos continúan deshabilitados en staging, y el Match real/rollback está probado por la suite CI aislada.
4. Se comprueba que no hay cambios en producción.

**La validación de los pagos deshabilitados es intencional:** no se debe activar el modo de pagos con el pretexto de cerrar P0.
