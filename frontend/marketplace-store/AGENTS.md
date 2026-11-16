## Reglas de integración LOGIMKT Marketplace

Lee este README completo antes de modificar el storefront. El contrato con el ERP está en src/lib/erp.ts y src/lib/erp-sync.ts.

- El SKU oficial del ERP es la llave entre sistemas. products.erp_id guarda el GUID del ERP.
- La tienda mantiene una base PostgreSQL independiente. Nunca escribir directamente en la base del ERP; toda integración se realiza por su API HTTP.
- El ERP es la fuente de verdad de catálogo, precios, stock, reservas, estado formal del pedido y RMA.
- La sincronización completa de catálogo requiere credenciales ERP Admin. Debe hacer upsert de productos nuevos, actualizar precios/stock/EAN y desactivar productos no publicados. No inventar correspondencias por nombre.
- No sincronizar catálogo si existen pedidos Pendiente ERP; primero reintentar o cancelar esos pedidos.
- Cuando ERP está configurado, una orden local queda Pendiente ERP hasta que el OMS confirme la reserva. No mostrarla como confirmada antes de la respuesta satisfactoria.
- Mantener el mismo código externo en los reintentos; el ERP aplica idempotencia.
- Avances de estado, cancelaciones y solicitudes de devolución deben enviarse al ERP. Cancelar en ERP antes de liberar stock local.
- Una devolución es una solicitud RMA pendiente de inspección. No reingresar stock ni marcar reembolso automáticamente.
- Pagos reales, tarjetas y pasarelas permanecen deshabilitados. Transferencia/crédito requieren validación manual; contra entrega se confirma después de la entrega.
- Sin variables ERP_*, el modo local de demostración puede funcionar, pero nunca debe afirmar que reservó inventario en el ERP.
- En Vercel, ADMIN_PASSWORD es obligatorio. No usar contraseñas por defecto en despliegues.
- Nunca subir .env*, contraseñas, JWT ni cadenas de conexión. No escribir secretos en logs o documentación.
