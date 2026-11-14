# Checkout comercial B2C multi-SKU V2

**Estado:** implementado y validado localmente en `MarketplaceERP-Candidate-Work`. Aún no está desplegado ni promovido a `main`.

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

## Pendientes para cerrar el Bloque C

- Validar el endpoint en Render STAGING con pagos deshabilitados y comprobar que no escribe datos.
- Extender el preflight administrativo de cierre para leer el mapa 1:N; el seguimiento operativo ya lo valida en modo lectura. No liberar reservas ante pagos inciertos sin evidencia verificable del proveedor.
- Ejecutar los recorridos B2B/B2C y matriz de permisos en el despliegue.
- Revisar logs y evidencias, actualizar Issue #65 y preparar PR #60.
- No fusionar a `main` hasta completar las puertas de validación.
