# Checkout comercial B2C multi-SKU V2

**Estado (13-nov-2026):** checkout comercial V2 desplegado en Render STAGING (`dep-dbrr0e6i0phs73b8d9lg`) y Vercel Preview (`dpl_7Giu5xw86tiXRDzpgHRgK3ySJV1x`), ambos en SHA `5922003`. No se ha promovido a `main` ni producción. La extensión del preflight 1:N descrita abajo se probó LOCALMENTE y requiere CI del nuevo commit.

## Contrato HTTP

- Método: `POST /api/checkout/v2/comprar`
- Autorización: únicamente rol `Comprador`
- Cabecera obligatoria: `Idempotency-Key` (8–128 caracteres alfanuméricos, guion o guion bajo)
- Cuerpo:

```json
{
  "items": [
    { "productoId": "GUID-DEL-PRODUCTO-1", "cantidad": 2 },
    { "productoId": "GUID-DEL-PRODUCTO-2", "cantidad": 1 }
  ]
}
```

El navegador no envía precio, vendedor, impuesto, comisión ni total. La API los obtiene y valida desde el catálogo.

## Reglas

- Entre 2 y 20 SKU distintos; se aceptan líneas repetidas y se normalizan por producto.
- Un único vendedor por orden.
- Productos publicados, activos, con precio B2C positivo y moneda USD.
- Cantidad de 1 a 9,999 por SKU.
- El precio se recalcula en el servidor al confirmar.
- Se conserva la política fiscal B2C de V1: IVA y envío adicionales en cero. No se introduce una política fiscal nueva.
- El checkout V1 de un solo producto no se modifica.

## Consistencia transaccional

1. Se abre una transacción en Finance.
2. Catalog usa la misma conexión PostgreSQL y la misma transacción.
3. Orden, líneas, intento de pago, reservas multi-SKU y mapa `finance.intento_reservas_pago` se escriben dentro de esa transacción.
4. El intento pasa a `Enviado` antes del commit y antes de invocar la pasarela.
5. Una aprobación se persiste como `Aprobado`; luego, en otra transacción compartida, se calcula la comisión, se crea el split, se confirman todas las reservas, se genera el asiento y se completa la orden.
6. Un rechazo confirmado libera todas las reservas y cancela la orden en una transacción.
7. Una excepción, respuesta ambigua o aprobación sin identificador verificable deja el intento en conciliación y conserva las reservas. No se reenvía el cobro al repetir la misma clave.

La tabla de relación 1:N ya existe en Neon STAGING aislado; esta implementación no agrega migraciones.

## Seguridad y límites de entorno

- `PaymentOperationsGuard` protege la ruta.
- Con pagos deshabilitados, la API responde HTTP 503 antes de crear orden, intento o reserva.
- Las pruebas locales usan un gateway simulado inyectado en el host de pruebas.
- No se activó ni se invocó una pasarela real.
- El simulador QA anterior continúa separado; no se habilitó en Render STAGING ni en producción.

## Evidencia local

- 5/5 pruebas HTTP multi-SKU con PostgreSQL efímero: aprobación, rechazo/compensación, resultado incierto, stock insuficiente/rollback y pagos deshabilitados.
- El seguimiento operativo valida de solo lectura la correspondencia 1:N entre líneas, intento y reservas, y alerta ante mapas o estados inconsistentes. Las mismas cinco pruebas comprueban seguimiento sin falsos positivos para aprobación, rechazo e incertidumbre.
- 37/37 pruebas de matriz de autorización, incluyendo el endpoint nuevo.
- 43 aserciones frontend de estado e idempotencia V1/V2.
- Frontend: lint, build, tests de autenticación y P3 aprobados.
- La ejecución completa de la solución alcanzó 659 pruebas aprobadas en Finance antes de que el test runner cancelara cuatro pruebas por bloqueo de un testhost; no se declara la suite completa como PASS.

## Preflight administrativo de solo lectura para multi-SKU (F266)

- `CierreCheckoutPreflight` reconoce **solo órdenes comerciales** `CHK2-*`, diferenciándolas del simulador QA `QA-V2-*`; mantiene la regla 1:1 de V1.
- Comprueba 2–20 SKU distintos y su subtotal, un único intento conciliado, relación 1:N sin duplicados ni enlaces huérfanos, cantidades originales, productos del vendedor correcto, reservas confirmadas y saldos físicos WMS suficientes.
- Un mapa adulterado bloquea el cierre con `MAPA_RESERVAS_MULTISKU_INCONSISTENTE`; estados de pago incierto, intentos incompletos, reembolsos, liquidaciones o logística iniciada siguen bloqueados. **No cambia estados ni reserva/libera inventario.** El prototipo administrativo sigue fuera de DI/HTTP y no es una autorización de cobro ni de cierre productivo.
- Pruebas POST/PG de multi-SKU local: **5/5 PASS** (incluye caso de modificación intencionada del mapa). Regresión V1 + recuperación QA + multi-SKU QA local: **21/21 PASS**. La base temporal se limita al prefijo `ci_checkout_http_multisku_` y el teardown espera la desconexión efectiva de sus sesiones; sin operaciones contra Neon.

## Pendientes para cerrar el Bloque C

- Validar en Render STAGING el POST con token de comprador auténtico, con `Payments:Provider=Disabled` y HTTP **503**, y comparar conteos de Neon antes/después sin crear órdenes.
- Certificar el preflight 1:N en CI del SHA final y comprobar el despliegue de ese SHA. No liberar reservas ante pagos inciertos sin confirmación verificable del proveedor.
- Ejecutar los recorridos B2B/B2C y matriz de permisos en el despliegue.
- Revisar logs y evidencias, actualizar Issue #65 y preparar PR #60.
- No fusionar a `main` hasta completar las puertas de validación.
