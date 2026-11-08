-- P2: huellas reproducibles sobre las 37 tablas de la version auditada
-- SOLO LECTURA. Ejecutar el mismo archivo en rama de origen y rama recuperada.
-- Comparar conteos y huellas. Incluye tablas vacias e historiales de migraciones.
-- MD5 es comprobacion operativa de coincidencia, no hash criptografico de backups.
-- SQL estatico conforme al esquema del commit bdee5414. No imprimir datos personales.
SELECT 'catalog.ItemsPickList' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "catalog"."ItemsPickList" t
UNION ALL
SELECT 'catalog.PaquetesDespacho' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "catalog"."PaquetesDespacho" t
UNION ALL
SELECT 'catalog.PickLists' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "catalog"."PickLists" t
UNION ALL
SELECT 'catalog.__EFMigrationsHistory' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "catalog"."__EFMigrationsHistory" t
UNION ALL
SELECT 'catalog.ajustes_inventario' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "catalog"."ajustes_inventario" t
UNION ALL
SELECT 'catalog.alertas_inventario_revision' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "catalog"."alertas_inventario_revision" t
UNION ALL
SELECT 'catalog.conteos_ciclicos' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "catalog"."conteos_ciclicos" t
UNION ALL
SELECT 'catalog.devoluciones_rma' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "catalog"."devoluciones_rma" t
UNION ALL
SELECT 'catalog.movimientos_inventario' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "catalog"."movimientos_inventario" t
UNION ALL
SELECT 'catalog.movimientos_mwip' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "catalog"."movimientos_mwip" t
UNION ALL
SELECT 'catalog.productos' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "catalog"."productos" t
UNION ALL
SELECT 'catalog.reservas_stock' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "catalog"."reservas_stock" t
UNION ALL
SELECT 'catalog.reservas_stock_detalle' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "catalog"."reservas_stock_detalle" t
UNION ALL
SELECT 'catalog.stock_fisico' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "catalog"."stock_fisico" t
UNION ALL
SELECT 'catalog.ubicaciones_almacen' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "catalog"."ubicaciones_almacen" t
UNION ALL
SELECT 'core.__EFMigrationsHistory' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "core"."__EFMigrationsHistory" t
UNION ALL
SELECT 'core.categorias' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "core"."categorias" t
UNION ALL
SELECT 'core.compradores' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "core"."compradores" t
UNION ALL
SELECT 'core.proveedores' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "core"."proveedores" t
UNION ALL
SELECT 'core.reglas_comision' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "core"."reglas_comision" t
UNION ALL
SELECT 'core.usuarios' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "core"."usuarios" t
UNION ALL
SELECT 'core.vendedores' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "core"."vendedores" t
UNION ALL
SELECT 'finance.__EFMigrationsHistory' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "finance"."__EFMigrationsHistory" t
UNION ALL
SELECT 'finance.conciliaciones_pago' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "finance"."conciliaciones_pago" t
UNION ALL
SELECT 'finance.facturas_proveedor' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "finance"."facturas_proveedor" t
UNION ALL
SELECT 'finance.intentos_pago' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "finance"."intentos_pago" t
UNION ALL
SELECT 'finance.items_factura_proveedor' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "finance"."items_factura_proveedor" t
UNION ALL
SELECT 'finance.items_orden' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "finance"."items_orden" t
UNION ALL
SELECT 'finance.items_orden_compra' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "finance"."items_orden_compra" t
UNION ALL
SELECT 'finance.lineas_partida_contable' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "finance"."lineas_partida_contable" t
UNION ALL
SELECT 'finance.liquidaciones' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "finance"."liquidaciones" t
UNION ALL
SELECT 'finance.matches_tridireccionales' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "finance"."matches_tridireccionales" t
UNION ALL
SELECT 'finance.ordenes' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "finance"."ordenes" t
UNION ALL
SELECT 'finance.ordenes_compra' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "finance"."ordenes_compra" t
UNION ALL
SELECT 'finance.partidas_contables' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "finance"."partidas_contables" t
UNION ALL
SELECT 'finance.reembolsos_pago' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "finance"."reembolsos_pago" t
UNION ALL
SELECT 'finance.splits_pago' AS tabla, COUNT(*)::bigint AS filas, md5(COALESCE(string_agg(md5(row_to_json(t)::text),'' ORDER BY md5(row_to_json(t)::text)),'')) AS huella FROM "finance"."splits_pago" t ORDER BY tabla;
