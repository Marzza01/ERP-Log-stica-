# P2 — Guion demostrable y recuperación sin alterar producción

## Antes de demostrar
Confirmar exactamente commit y destino de Preview, Render y Neon. Usar exclusivamente staging aislado o clon; nunca credenciales ni secretos en capturas. Las 500 fichas de productos **no** equivalen a 500 stock disponible. `Payments:Provider=Disabled`; liquidaciones son registros contables simulados, no transferencias bancarias.

## Guion por módulo: entrada → pasos → evidencia → condición de éxito

1. **Login (Admin).** Cuenta Admin activa; entrar, recargar, abrir módulo solo Admin, salir y volver a cargar. Evidencia: E2E Preview + Render + Neon ya aceptado `P1_PREVIEW_EVIDENCE.completed=true`; volver a verificar solo si el SHA funcional cambia.
2. **Catálogo.** `/productos`; buscar 2 SKU conocidos, revisar nombres, precios y paginación. Esperado: 500 SKU únicos y ningún SKU vacío. Evidencia: consultas P2; capturas visuales pendientes.
3. **Compras/proveedores.** `/proveedores`, `/compras`; localizar 4 proveedores y 3 PO (1 Borrador, 2 RecibidaCompleta); consultar las líneas y 1 factura. Esperado: estados y importes coherentes. Evidencia: SQL staging y P0, ejecución visual pendiente.
4. **WMS/inventario.** `/wms`; revisar 4 ubicaciones, 3 movimientos y 2 stocks con 10 unidades en total. Esperado: no inventar inventario disponible en los otros 498 SKU. Evidencia: SQL staging; UI pendiente.
5. **Facturación/conciliación.** `/contabilidad`; presentar factura de proveedor y distinción entre comprobación de factura, asiento interno y pago externo. No generar 3-way Match o asientos en staging. Evidencia: FIN07 y P0/P1; escenario integral con fixture pendiente.
6. **Checkout Comprador.** No hay cuenta Comprador en Neon staging: preparar una exclusivamente en sandbox con bootstrap/proceso legítimo; usar API real y stock autorizado. Esperado: idempotencia y reservas sin overselling. Evidencia: suites CI; demo visual pendiente.
7. **Órdenes/Vendedor/fulfillment.** Sin órdenes de cliente ni cuenta Vendedor en staging: preparar escenario sandbox con transiciones reales, picking, packing, guía simulada. Esperado: estados consistentes y sin stock doble. Evidencia: código/tests; demo visual pendiente.
8. **Liquidaciones.** Sin liquidaciones persistidas; demostrar únicamente mecanismo simulado en sandbox, manteniendo pagos externos bloqueados. No afirmar confirmación bancaria. Evidencia: tests P1 de semántica; UI con fixture pendiente.
9. **Dashboard.** `/dashboard` con Admin; tomar captura y contrastar métricas con consultas y filtros. Si no hay denominador/datos suficientes, mostrar sin datos, nunca porcentajes inventados.

## Datos sintéticos requeridos para P2.2
Crear **únicamente en una base desechable con API configurada al clon**: 1 Admin, 1 Vendedor, 1 Comprador con credenciales temporales; 2 SKU con stock suficiente y ubicaciones; 2 PO; una factura; 2 recepciones; 3 órdenes de venta; 2 reservas; al menos una operación de picking/packing; una simulación contable si el motor lo soporta con pagos Disabled. No insertar a mano `finance.ordenes`, `splits_pago` ni `liquidaciones`; el alta debe pasar por servicios de negocio, permisos y controles de idempotencia. No publicar cuentas de prueba fuera del entorno aislado.

## Respaldo, ensayo de recuperación y rollback
1. Comprobar cuenta/proyecto/branch/DB, SHA desplegado y permisos mínimos. Guardar en registro privado nombres e IDs de destino; **nunca** cadenas de conexión o contraseñas.
2. Congelar nuevas escrituras en el **clon** antes del respaldo. Identificar punto consistente o snapshot y retención. Registrar conteos, migraciones y resultados de `scripts/p2-integridad-readonly.sql`.
3. Crear snapshot del clon, restaurar sobre **otra rama nueva**; no restaurar encima de staging o Neon main.
4. Comparar conteos de todos los módulos, IDs y totales de PO/factura, referencias, estados, migraciones de los tres contextos, duplicados, stock y asientos. Verificar una lectura API auténtica del entorno restaurado; resultado 200 y controles 401/403.
5. Si no coincide, detener la prueba. No ejecutar comandos EF automáticos para “arreglar” el respaldo.
6. En una promoción real: respaldo verificable y rollback de API/Preview a SHA anterior, con evaluación independiente de rollback de schema. **No usar EF Down a ciegas**: FIN06 incluye índices únicos cuya reversión elimina barreras de integridad.
7. Documentar hora, branch origen/restaurado, resúmenes de filas, evidencia y responsable. Destruir recursos temporales solo con autorización.

## Preparación de merge
- Revisar 138 commits y 134 archivos vs main por categorías: 57 `src`, 42 `tests`, 12 `frontend`, 12 `docs` y otros.
- PR #2 aún abierto, técnicamente mergeable, pero divergente de staging. Determinar si sustituirlo por una promoción supervisada; **no cerrarlo ni mergearlo automáticamente**.
- Confirmar historia EF en destino y ensayar el plan contra clon; realizar CI y E2E del SHA definitivo, validar health/ready y errores de Render/Vercel, registrar reversión.
- No confundir documentación de ensayo previo con estado actual de FIN06/FIN07: hoy ya constan en staging.

## Cierre P2
Cada módulo requiere URL/SHA, conjunto de datos usado, actor/rol, resultado esperado/observado, captura o traza de ejecución y observaciones; cinco criterios P2 firmados. Sin prueba de restauración, datos de demo y recorrido navegador, **no declarar 100 %**.
