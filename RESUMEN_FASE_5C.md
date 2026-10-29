# Resumen Fase 5C — Reabastecimiento operativo

La Fase 5C agrega una capa de decisión logística sobre el stock real.

El módulo identifica productos en o por debajo de `StockMinimo`, calcula la disponibilidad agregada considerando reservado y bloqueado, y cruza la necesidad con órdenes de compra todavía abiertas.

El objetivo es evitar dos problemas:

1. detectar tarde un faltante de inventario;
2. generar compras duplicadas cuando ya existe una PO en curso para ese SKU.

No se crea una nueva tabla y no se modifica el flujo financiero. La creación y aprobación formal de órdenes de compra sigue perteneciendo a Compras.

El siguiente bloque previsto es la Fase 5D — Proveedores logísticos, donde se conectará el proveedor indicado en Catálogo con el maestro `core.proveedores` y sus métricas logísticas.

`CodigoEAN` continúa sin cambios y pendiente para otro integrante.
