# U2 / U2.1 — Cierre transversal de autorización

Base: `13e864fe5ece4559c498ab311859f402f90083ea`. Integración y respaldo U0: `abac4f34aad02cec20a1e3376c5305a98d5ade82`, con contenido API idéntico. Sin merge, staging ni commit. Este documento sustituye el veredicto previo: las restricciones de rol originales no demostraban aislamiento entre vendedores.

## Inventario y decisión final

23 controladores, 83 acciones HTTP, 84 combinaciones método/ruta (GetAlertasStock tiene dos alias), más dos rutas de salud en Program. Resultado: **81 rutas de controladores Admin, una pública de login y dos excepciones de Comprador**. Ninguna ruta admite acceso por Vendedor. Todas las acciones privadas rechazan anónimos.

Se restringen temporalmente a Admin todas las operaciones y listados sobre inventario, WMS, RMA, fulfillment, órdenes, productos (incluido catálogo con stock), vendedores, proveedores y reabastecimiento sin ámbito demostrado. Compras, contabilidad, KPIs, liquidaciones, comisiones, usuarios, recuperación de pagos y expiración de reservas permanecen/requieren Admin. No se añaden excepciones AllowAnonymous. No se usa un ID o nombre enviado por el cliente como prueba de identidad.

La limitación Admin no implementa propiedad ni cambia los cuerpos de acciones. Es una barrera temporal que elimina el acceso de Vendedor y Comprador a los recursos privados globales. Incluso los productos publicados devuelven stocks/datos operativos; no se presume que ese contrato sea una vista pública segura. Catálogo anónimo no existía y no se incorpora.

## Matriz exhaustiva

Metadatos efectivos combinados con AND; C/A muestran el origen. “Lista/global” indica ausencia de ámbito por propietario, aunque haya filtros operativos. Los filtros operativos no se utilizan como identidad. Las columnas base e integración conservan la comparación previa. Los cuerpos no se ejecutaron.

| Método y ruta | Roles efectivos U2.1 | Origen (C controlador; A acción) | Recurso | ID/nombre del cliente | Lista/global | Propietario demostrado | Política requerida | Base U1b | Integración/respaldo |
|---|---|---|---|---|---|---|---|---|---|
| GET `/api/inventario/ajustes` | Admin | C: Admin; A: — | Ajuste y saldo de stock | No ID/nombre (filtros operativos si admite) | Sí, listado/agregado sin ámbito vendedor | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado | Ausente |
| POST `/api/inventario/ajustes` | Admin ∧ Admin | C: Admin; A: Admin | Ajuste y saldo de stock | Sí, cuerpo/archivo; no identidad | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado ∧ Admin,Vendedor | Ausente |
| POST `/api/inventario/ajustes/{id:guid}/aprobar` | Admin ∧ Admin | C: Admin; A: Admin | Ajuste y saldo de stock | Sí, ID de ruta | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado ∧ Admin | Ausente |
| POST `/api/inventario/ajustes/{id:guid}/rechazar` | Admin ∧ Admin | C: Admin; A: Admin | Ajuste y saldo de stock | Sí, ID de ruta | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado ∧ Admin | Ausente |
| POST `/api/inventario/ajustes/{id:guid}/cancelar` | Admin ∧ Admin | C: Admin; A: Admin | Ajuste y saldo de stock | Sí, ID de ruta | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado ∧ Admin,Vendedor | Ausente |
| POST `/api/inventario/ajustes/{id:guid}/aplicar` | Admin ∧ Admin | C: Admin; A: Admin | Ajuste y saldo de stock | Sí, ID de ruta | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado ∧ Admin | Ausente |
| GET `/api/alertas-inventario` | Admin | C: Admin; A: — | Alertas, stock y compras | No ID/nombre (filtros operativos si admite) | Sí, listado/agregado sin ámbito vendedor | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado | Ausente |
| POST `/api/alertas-inventario/{productoId:guid}/reconocer` | Admin | C: Admin; A: — | Alertas, stock y compras | Sí, productoId y request.Usuario | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado | Ausente |
| POST `/api/auth/login` | Público | C: —; A: Público | Cuenta y token | Email/Username y contraseña: credenciales | No (operación individual/configuración) | N/A: valida cuenta activa y hash | Público | Público | Público |
| POST `/api/inventario/bloqueos/bloquear` | Admin ∧ Admin | C: Admin; A: Admin | Saldo de stock | Sí, cuerpo/archivo; no identidad | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado ∧ Admin,Vendedor | Ausente |
| POST `/api/inventario/bloqueos/desbloquear` | Admin ∧ Admin | C: Admin; A: Admin | Saldo de stock | Sí, cuerpo/archivo; no identidad | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado ∧ Admin,Vendedor | Ausente |
| POST `/api/checkout` | Comprador | C: —; A: Comprador | Compra/orden | Sí, cuerpo/archivo; no identidad | No (operación individual/configuración) | Sí comprador: NameIdentifier; idempotencia por CompradorId | Comprador | Comprador | Comprador |
| GET `/api/checkout/ordenes` | Admin | C: —; A: Admin | Compra/orden | No ID/nombre (filtros operativos si admite) | Sí, listado/agregado sin ámbito vendedor | No demostrado; Admin es barrera, no propiedad | Admin | Admin | Admin |
| GET `/api/checkout/recuperacion/capacidades` | Admin | C: Admin; A: — | Orden y pago | No ID/nombre (filtros operativos si admite) | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Admin | Ausente |
| POST `/api/checkout/recuperacion/ordenes/{ordenId:guid}/conciliar` | Admin | C: Admin; A: — | Orden y pago | Sí, ID de ruta | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Admin | Ausente |
| POST `/api/checkout/recuperacion/ordenes/{ordenId:guid}/reembolsar` | Admin | C: Admin; A: — | Orden y pago | Sí, ID de ruta | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Admin | Ausente |
| GET `/api/checkout/ordenes/{ordenId:guid}/seguimiento` | Admin,Comprador | C: —; A: Admin,Comprador | Orden y pago | Sí, ID de ruta | No (operación individual/configuración) | Sí comprador: compara orden.CompradorId con NameIdentifier | Admin,Comprador | Admin,Comprador | Ausente |
| GET `/api/checkout/pendientes-conciliacion` | Admin | C: —; A: Admin | Orden y pago | No ID/nombre (filtros operativos si admite) | Sí, listado/agregado sin ámbito vendedor | No demostrado; Admin es barrera, no propiedad | Admin | Admin | Ausente |
| GET `/api/comisiones` | Admin | C: Admin; A: — | Reglas de comisión | No ID/nombre (filtros operativos si admite) | Sí, listado/agregado sin ámbito vendedor | No demostrado; Admin es barrera, no propiedad | Admin | Admin | Admin |
| POST `/api/comisiones` | Admin | C: Admin; A: — | Reglas de comisión | Sí, cuerpo/archivo; no identidad | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Admin | Admin |
| GET `/api/Compras/po` | Admin | C: Admin; A: — | Orden de compra/factura | No ID/nombre (filtros operativos si admite) | Sí, listado/agregado sin ámbito vendedor | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado (fallback) | Admin |
| GET `/api/Compras/po/{id:guid}` | Admin | C: Admin; A: — | Orden de compra/factura | Sí, ID de ruta | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado (fallback) | Admin |
| POST `/api/Compras/po` | Admin | C: Admin; A: — | Orden de compra/factura | Sí, cuerpo/archivo; no identidad | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado (fallback) | Admin |
| POST `/api/Compras/po/{id:guid}/enviar` | Admin | C: Admin; A: — | Orden de compra/factura | Sí, ID de ruta | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado (fallback) | Ausente |
| PUT `/api/Compras/po/{id:guid}/recibir` | Admin | C: Admin; A: — | Orden de compra/factura | Sí, ID de ruta | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado (fallback) | Admin |
| POST `/api/Compras/facturas` | Admin | C: Admin; A: — | Orden de compra/factura | Sí, cuerpo/archivo; no identidad | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado (fallback) | Admin |
| POST `/api/Compras/match` | Admin | C: Admin; A: — | Orden de compra/factura | Sí, cuerpo/archivo; no identidad | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado (fallback) | Admin |
| GET `/api/Compras/match/historial` | Admin | C: Admin; A: — | Orden de compra/factura | No ID/nombre (filtros operativos si admite) | Sí, listado/agregado sin ámbito vendedor | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado (fallback) | Admin |
| GET `/api/Contabilidad/libro-diario` | Admin | C: Admin; A: — | Asientos y libros | No ID/nombre (filtros operativos si admite) | Sí, listado/agregado sin ámbito vendedor | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado (fallback) | Admin |
| GET `/api/Contabilidad/libro-mayor` | Admin | C: Admin; A: — | Asientos y libros | No ID/nombre (filtros operativos si admite) | Sí, listado/agregado sin ámbito vendedor | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado (fallback) | Admin |
| GET `/api/Contabilidad/balance-comprobacion` | Admin | C: Admin; A: — | Asientos y libros | No ID/nombre (filtros operativos si admite) | Sí, listado/agregado sin ámbito vendedor | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado (fallback) | Admin |
| POST `/api/Contabilidad/asiento-manual` | Admin | C: Admin; A: — | Asientos y libros | Sí, cuerpo/archivo; no identidad | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado (fallback) | Admin |
| GET `/api/inventario/conteos-ciclicos` | Admin | C: Admin; A: — | Conteo y saldo de stock | No ID/nombre (filtros operativos si admite) | Sí, listado/agregado sin ámbito vendedor | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado | Ausente |
| POST `/api/inventario/conteos-ciclicos` | Admin ∧ Admin | C: Admin; A: Admin | Conteo y saldo de stock | Sí, cuerpo/archivo; no identidad | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado ∧ Admin,Vendedor | Ausente |
| POST `/api/inventario/conteos-ciclicos/{id:guid}/iniciar` | Admin ∧ Admin | C: Admin; A: Admin | Conteo y saldo de stock | Sí, ID de ruta | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado ∧ Admin,Vendedor | Ausente |
| POST `/api/inventario/conteos-ciclicos/{id:guid}/primer-conteo` | Admin ∧ Admin | C: Admin; A: Admin | Conteo y saldo de stock | Sí, ID de ruta | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado ∧ Admin,Vendedor | Ausente |
| POST `/api/inventario/conteos-ciclicos/{id:guid}/reconteo` | Admin ∧ Admin | C: Admin; A: Admin | Conteo y saldo de stock | Sí, ID de ruta | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado ∧ Admin,Vendedor | Ausente |
| POST `/api/inventario/conteos-ciclicos/{id:guid}/cancelar` | Admin ∧ Admin | C: Admin; A: Admin | Conteo y saldo de stock | Sí, ID de ruta | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado ∧ Admin,Vendedor | Ausente |
| POST `/api/fulfillment/ordenes/{ordenId:guid}/generar-picklist` | Admin | C: Admin; A: — | Orden/picklist/paquete | Sí, ordenId y operador | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Admin,Vendedor | Admin,Vendedor |
| POST `/api/fulfillment/picklists/{pickListId:guid}/confirmar` | Admin | C: Admin; A: — | Orden/picklist/paquete | Sí, ID de ruta | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Admin,Vendedor | Admin,Vendedor |
| POST `/api/fulfillment/ordenes/{ordenId:guid}/empacar` | Admin | C: Admin; A: — | Orden/picklist/paquete | Sí, ID de ruta | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Admin,Vendedor | Admin,Vendedor |
| POST `/api/fulfillment/paquetes/{paqueteId:guid}/despachar` | Admin | C: Admin; A: — | Orden/picklist/paquete | Sí, ID de ruta | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Admin,Vendedor | Admin,Vendedor |
| GET `/api/fulfillment/picklists` | Admin | C: Admin; A: — | Orden/picklist/paquete | No ID/nombre (filtros operativos si admite) | Sí, listado/agregado sin ámbito vendedor | No demostrado; Admin es barrera, no propiedad | Admin | Admin,Vendedor | Admin,Vendedor |
| GET `/api/fulfillment/paquetes` | Admin | C: Admin; A: — | Orden/picklist/paquete | No ID/nombre (filtros operativos si admite) | Sí, listado/agregado sin ámbito vendedor | No demostrado; Admin es barrera, no propiedad | Admin | Admin,Vendedor | Admin,Vendedor |
| GET `/api/kpis-dashboard` | Admin | C: Admin; A: — | Métricas globales | No ID/nombre (filtros operativos si admite) | Sí, listado/agregado sin ámbito vendedor | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado (fallback) | Admin |
| GET `/api/kpis-dashboard/modelo-financiero` | Admin | C: Admin; A: — | Métricas globales | No ID/nombre (filtros operativos si admite) | Sí, listado/agregado sin ámbito vendedor | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado (fallback) | Admin |
| POST `/api/Liquidaciones/generar` | Admin | C: Admin; A: — | Liquidación/vendedor | Sí, cuerpo/archivo; no identidad | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado (fallback) | Autenticado ∧ Admin |
| POST `/api/Liquidaciones/procesar-pago/{id:guid}` | Admin | C: Admin; A: — | Liquidación/vendedor | Sí, ID de ruta | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado (fallback) | Autenticado ∧ Admin |
| GET `/api/Liquidaciones/historial` | Admin | C: Admin; A: — | Liquidación/vendedor | Sí, vendedorId del cliente; solo Admin | Sí, listado/agregado sin ámbito vendedor | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado (fallback) | Autenticado ∧ Admin,Vendedor |
| GET `/api/Liquidaciones/kpis-vendedores` | Admin | C: Admin; A: — | Liquidación/vendedor | No ID/nombre (filtros operativos si admite) | Sí, listado/agregado sin ámbito vendedor | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado (fallback) | Autenticado ∧ Admin |
| GET `/api/Liquidaciones/pendientes` | Admin | C: Admin; A: — | Liquidación/vendedor | No ID/nombre (filtros operativos si admite) | Sí, listado/agregado sin ámbito vendedor | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado (fallback) | Autenticado ∧ Admin |
| GET `/api/Ordenes` | Admin | C: Admin; A: — | Orden y métricas | No ID/nombre (filtros operativos si admite) | Sí, listado/agregado sin ámbito vendedor | No demostrado; Admin es barrera, no propiedad | Admin | Admin,Vendedor | Autenticado ∧ Admin,Vendedor |
| GET `/api/Ordenes/{id:guid}` | Admin | C: Admin; A: — | Orden y métricas | Sí, ID de ruta | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Admin,Vendedor | Autenticado ∧ Admin,Vendedor |
| POST `/api/Ordenes` | Admin ∧ Admin | C: Admin; A: Admin | Orden y métricas | Sí, cuerpo/archivo; no identidad | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Admin,Vendedor | Autenticado ∧ Admin |
| PUT `/api/Ordenes/{id:guid}/estado` | Admin | C: Admin; A: — | Orden y métricas | Sí, ID de ruta | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Admin,Vendedor | Autenticado ∧ Admin,Vendedor |
| GET `/api/Ordenes/kpis` | Admin | C: Admin; A: — | Orden y métricas | No ID/nombre (filtros operativos si admite) | Sí, listado/agregado sin ámbito vendedor | No demostrado; Admin es barrera, no propiedad | Admin | Admin,Vendedor | Autenticado ∧ Admin,Vendedor |
| GET `/api/productos` | Admin | C: Admin; A: — | Producto/stock | No ID/nombre (filtros operativos si admite) | Sí, listado/agregado sin ámbito vendedor | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado | Autenticado |
| GET `/api/productos/buscar` | Admin | C: Admin; A: — | Producto/stock | Sí, q/categoría/subcategoría; no identidad | Sí, listado/agregado sin ámbito vendedor | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado | Autenticado |
| GET `/api/productos/b2b` | Admin | C: Admin; A: — | Producto/stock | Sí, categoría; no identidad | Sí, listado/agregado sin ámbito vendedor | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado | Autenticado |
| GET `/api/productos/alertas-stock` | Admin | C: Admin; A: — | Producto/stock | No ID/nombre (filtros operativos si admite) | Sí, listado/agregado sin ámbito vendedor | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado | Autenticado |
| GET `/api/productos/alertas-reorden` | Admin | C: Admin; A: — | Producto/stock | No ID/nombre (filtros operativos si admite) | Sí, listado/agregado sin ámbito vendedor | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado | Autenticado |
| POST `/api/productos/importar-csv` | Admin ∧ Admin | C: Admin; A: Admin | Producto/stock | Sí, cuerpo/archivo; no identidad | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado ∧ Admin,Vendedor | Autenticado ∧ Admin,Vendedor |
| POST `/api/productos/seed-500` | Admin ∧ Admin | C: Admin; A: Admin | Producto/stock | No | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado ∧ Admin | Autenticado ∧ Admin |
| GET `/api/productos/{id:guid}` | Admin | C: Admin; A: — | Producto/stock | Sí, ID de ruta | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado | Autenticado |
| POST `/api/productos` | Admin ∧ Admin | C: Admin; A: Admin | Producto/stock | Sí, cuerpo/archivo; no identidad | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado ∧ Vendedor,Admin | Autenticado ∧ Vendedor,Admin |
| PUT `/api/productos/{id:guid}` | Admin ∧ Admin | C: Admin; A: Admin | Producto/stock | Sí, ID de ruta | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado ∧ Vendedor,Admin | Ausente |
| DELETE `/api/productos/{id:guid}` | Admin ∧ Admin | C: Admin; A: Admin | Producto/stock | Sí, ID de ruta | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado ∧ Vendedor,Admin | Ausente |
| PATCH `/api/productos/{id:guid}/publicar` | Admin ∧ Admin | C: Admin; A: Admin | Producto/stock | Sí, ID de ruta | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado ∧ Vendedor,Admin | Autenticado ∧ Vendedor,Admin |
| GET `/api/proveedores/logistica` | Admin | C: Admin; A: — | Proveedor, compras y stock | Sí, filtros de consulta; no identidad | Sí, listado/agregado sin ámbito vendedor | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado | Ausente |
| GET `/api/reabastecimiento/necesidades` | Admin | C: Admin; A: — | Stock y compras | No ID/nombre (filtros operativos si admite) | Sí, listado/agregado sin ámbito vendedor | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado | Ausente |
| GET `/api/reservas/expiracion/pendientes` | Admin | C: Admin; A: — | Reservas/órdenes | No ID/nombre (filtros operativos si admite) | Sí, listado/agregado sin ámbito vendedor | No demostrado; Admin es barrera, no propiedad | Admin | Admin | Ausente |
| POST `/api/reservas/expiracion/ejecutar` | Admin | C: Admin; A: — | Reservas/órdenes | Sí, cuerpo/archivo; no identidad | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Admin | Ausente |
| POST `/api/rma` | Admin | C: Admin; A: — | Devolución/producto | Sí, cuerpo/archivo; no identidad | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado | Admin,Vendedor |
| POST `/api/rma/{id:guid}/inspeccionar` | Admin ∧ Admin | C: Admin; A: Admin | Devolución/producto | Sí, ID de ruta | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado ∧ Admin,Vendedor | Admin,Vendedor ∧ Admin,Vendedor |
| GET `/api/rma` | Admin | C: Admin; A: — | Devolución/producto | No ID/nombre (filtros operativos si admite) | Sí, listado/agregado sin ámbito vendedor | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado | Admin,Vendedor |
| POST `/api/usuarios` | Admin | C: Admin; A: — | Cuenta/rol/vendedor | Sí, cuerpo/archivo; no identidad | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Admin | Admin |
| GET `/api/vendedores` | Admin ∧ Admin | C: Admin; A: Admin | Vendedor | No ID/nombre (filtros operativos si admite) | Sí, listado/agregado sin ámbito vendedor | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado ∧ Admin,Vendedor | Autenticado ∧ Admin,Vendedor |
| GET `/api/vendedores/{id:guid}` | Admin ∧ Admin | C: Admin; A: Admin | Vendedor | Sí, ID de ruta | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado ∧ Admin,Vendedor | Autenticado ∧ Admin,Vendedor |
| POST `/api/vendedores` | Admin ∧ Admin | C: Admin; A: Admin | Vendedor | Sí, cuerpo/archivo; no identidad | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado ∧ Admin | Autenticado ∧ Admin |
| GET `/api/wms/mapa` | Admin | C: Admin; A: — | Ubicación/saldo | No ID/nombre (filtros operativos si admite) | Sí, listado/agregado sin ámbito vendedor | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado | Admin,Vendedor |
| GET `/api/wms/ubicaciones` | Admin | C: Admin; A: — | Ubicación/saldo | Sí, pasillo; no identidad | Sí, listado/agregado sin ámbito vendedor | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado | Admin,Vendedor |
| GET `/api/wms/stocks` | Admin | C: Admin; A: — | Ubicación/saldo | Sí, ubicacionId/sku; no identidad | Sí, listado/agregado sin ámbito vendedor | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado | Ausente |
| POST `/api/wms/transferir` | Admin ∧ Admin | C: Admin; A: Admin | Ubicación/saldo | Sí, cuerpo/archivo; no identidad | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado ∧ Admin,Vendedor | Ausente |
| POST `/api/wms/reubicar` | Admin ∧ Admin | C: Admin; A: Admin | Ubicación/saldo | Sí, cuerpo/archivo; no identidad | No (operación individual/configuración) | No demostrado; Admin es barrera, no propiedad | Admin | Autenticado ∧ Admin,Vendedor | Admin,Vendedor ∧ Admin,Vendedor |
| GET `/health` | Público | AllowAnonymous al mapear | Estado HTTP | No | No | N/A | Público | Público | Público |
| GET `/health/ready` | Público | AllowAnonymous al mapear | Disponibilidad de bases | No | No | N/A | Público | Público | Público |

## Requisitos para devolver acceso

- Órdenes y fulfillment: resolver cada orden/picklist/paquete a un vendedor autorizado desde el token; filtrar listados y KPIs; probar lectura y transiciones de un recurso ajeno, incluidos IDs relacionados divergentes. Corresponde a U3 y unidades posteriores; no se implementó.
- WMS, bloqueos, ajustes y conteos: resolver stock/producto/ubicaciones y operaciones vinculadas al ámbito permitido; verificar tanto origen como destino; filtrar listados y probar accesos cruzados, concurrencia y escrituras. El nombre auditado no equivale a autorización.
- RMA: verificar orden, producto y devolución vinculados al propietario; filtrar historial y probar solicitud/inspección ajenas.
- Productos: derivar vendedor de identidad autorizada para altas/importaciones; comprobar propietario antes de actualizar/eliminar/publicar. Separar una proyección comercial pública de stocks y datos operativos antes de habilitar catálogo a otros roles.
- Vendedores, proveedores, reabastecimiento y alertas: definir ámbito explícito y proyección mínima; filtrar compras/stock/alertas y probar referencias cruzadas.
- Liquidaciones: historial del vendedor solo con VendedorId válido del token y filtro obligatorio; nunca con el filtro del cliente como autoridad. Generación, pagos y métricas globales siguen administrativos.

No se mantiene Vendedor en ninguna ruta. Se conserva Comprador en POST /api/checkout: identidad de NameIdentifier y búsqueda de idempotencia por CompradorId (CheckoutController). Se conserva GET /api/checkout/ordenes/{ordenId}/seguimiento para Admin/Comprador: el código compara orden.CompradorId con NameIdentifier y devuelve NotFound para orden ajena (CheckoutSeguimientoController). Esta es evidencia estática de las comprobaciones existentes, no una prueba de propiedad ejecutada. Admin solo no obtiene acceso al POST de comprador: se conserva su rol exclusivo.

## JWT y login de U2

Program usa ApiSecurity: firma y expiración obligatorias, HS256, emisor/audiencia no vacíos, ClockSkew cero, duración configurada de 1 a 1440 minutos, clave externa de al menos 32 caracteres y rechazo de placeholders en todo entorno. NameIdentifier/sub mapeado debe ser GUID no vacío y concordante. Login conserva PasswordHasher contra cuenta activa, limitador y claims existentes; el hash ficticio de timing usa material aleatorio, no una credencial operativa. U2.1 no cambió ese cuerpo ni la generación de tokens. Longitud no demuestra entropía de la clave externa.

## Validación y límites

API y dependencias: dotnet build --no-restore, compilación final con cero errores y advertencias. **29 casos aislados aprobados, cero fallidos**: middleware real de autenticación/autorización en memoria y metadatos heredados/de acción. El caso exhaustivo recorre 23 controladores, 83 acciones y 84 alias método/ruta; por ruta verifica anónimo, Vendedor, Comprador y Admin, además de tokens expirados y firma incorrecta en las privadas. Las únicas excepciones se codifican expresamente en la prueba; AllowAnonymous accidental causa fallo. Se mantienen los casos de emisor, audiencia, algoritmo, expiración ausente, identidad inválida/ausente/contradictoria, token malformado y configuración JWT inválida.

Se compila la misma fuente de prueba mediante Roslyn/.NET 8 y referencias locales, invocando Fact/Theory con runner aislado; no es ejecución del runner xUnit completo ni prueba HTTP real. La compilación manual produce dos advertencias CS1701 de referencias xUnit reutilizadas. No prueba enrutamiento, filtros MVC completos, model binding ni cuerpos; tampoco demuestra propiedad de recursos. Los alias usan los mismos metadatos de acción, sin simular resolución de rutas. La suite Finance completa queda omitida por assets previos incompatibles con sus referencias actuales; no se restauraron paquetes.

Quedan pendientes login contra datos reales, revocación de tokens al desactivar cuentas/cambiar roles, bootstrap, contraseñas históricas, y autorización funcional de U3. El arranque real todavía puede migrar (unidad posterior); no se arrancó. Health conserva su acceso anónimo, incluido ready que consultaría bases; no se invocó. Swagger Development conserva la exposición heredada. Los nombres de auditoría aportados al cuerpo por un Admin no se certifican como atribución fiable y requieren reconciliación posterior; su contenido no concede acceso. No se certifica la seguridad global de checkout/reservas/pagos ni se habilita producción.

Sin cambios de esquema, servicios, repositorios, modelos, DTO, frontend o lógica de negocio en U2.1. Las funcionalidades operativas de vendedor, catálogo para comprador y consultas globales de otros roles quedan temporalmente bloqueadas hasta la reconciliación correspondiente. Este cierre permite revisar un commit acotado de políticas; no autoriza despliegue, merge ni inicio de servicios.

## Archivos pendientes U2/U2.1

- `src/MarketplaceERP.API/Controllers/AjustesInventarioController.cs`
- `src/MarketplaceERP.API/Controllers/AlertasInventarioController.cs`
- `src/MarketplaceERP.API/Controllers/AuthController.cs`
- `src/MarketplaceERP.API/Controllers/BloqueosInventarioController.cs`
- `src/MarketplaceERP.API/Controllers/ComprasController.cs`
- `src/MarketplaceERP.API/Controllers/ContabilidadController.cs`
- `src/MarketplaceERP.API/Controllers/ConteosCiclicosController.cs`
- `src/MarketplaceERP.API/Controllers/FulfillmentController.cs`
- `src/MarketplaceERP.API/Controllers/KpisDashboardController.cs`
- `src/MarketplaceERP.API/Controllers/LiquidacionesController.cs`
- `src/MarketplaceERP.API/Controllers/OrdenesController.cs`
- `src/MarketplaceERP.API/Controllers/ProductosController.cs`
- `src/MarketplaceERP.API/Controllers/ProveedoresController.cs`
- `src/MarketplaceERP.API/Controllers/ReabastecimientoController.cs`
- `src/MarketplaceERP.API/Controllers/RmaController.cs`
- `src/MarketplaceERP.API/Controllers/VendedoresController.cs`
- `src/MarketplaceERP.API/Controllers/WmsController.cs`
- `src/MarketplaceERP.API/Program.cs`
- `src/MarketplaceERP.API/Security/ApiSecurity.cs`
- `tests/MarketplaceERP.Finance.Tests/AutorizacionU2Tests.cs`
- `docs/Autenticacion_Autorizacion_U2.md`
