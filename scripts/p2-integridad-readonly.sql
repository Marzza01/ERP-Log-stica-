-- LOGIMKT P2 | auditoría de solo lectura | PostgreSQL Neon
-- Ejecutar únicamente después de comprobar project_id, branch_id, db y rol.
-- No usa INSERT/UPDATE/DELETE/DDL. Un cero en las anomalías no prueba flujos de tablas vacías.
SELECT current_database() AS database_name, current_setting('server_version') AS postgres_version;
SELECT 'productos' recurso,COUNT(*) cantidad FROM catalog.productos
UNION ALL SELECT 'proveedores',COUNT(*) FROM core.proveedores
UNION ALL SELECT 'usuarios',COUNT(*) FROM core.usuarios
UNION ALL SELECT 'compras',COUNT(*) FROM finance.ordenes_compra
UNION ALL SELECT 'facturas_proveedor',COUNT(*) FROM finance.facturas_proveedor
UNION ALL SELECT 'ordenes_cliente',COUNT(*) FROM finance.ordenes
UNION ALL SELECT 'movimientos_wms',COUNT(*) FROM catalog.movimientos_inventario
UNION ALL SELECT 'stock_fisico',COUNT(*) FROM catalog.stock_fisico
UNION ALL SELECT 'reservas',COUNT(*) FROM catalog.reservas_stock
UNION ALL SELECT 'picklists',COUNT(*) FROM catalog."PickLists"
UNION ALL SELECT 'liquidaciones',COUNT(*) FROM finance.liquidaciones
UNION ALL SELECT 'asientos',COUNT(*) FROM finance.partidas_contables
ORDER BY recurso;
SELECT "Rol","Activo",COUNT(*) AS cantidad FROM core.usuarios GROUP BY "Rol","Activo" ORDER BY "Rol","Activo";
SELECT "Estado",COUNT(*) AS cantidad FROM finance.ordenes_compra GROUP BY "Estado";
SELECT 'sku_duplicados' AS control,COUNT(*) AS anomalias FROM
  (SELECT "Sku" FROM catalog.productos GROUP BY "Sku" HAVING COUNT(*)>1) duplicados
UNION ALL SELECT 'sku_vacio',COUNT(*) FROM catalog.productos WHERE "Sku" IS NULL OR btrim("Sku")=''
UNION ALL SELECT 'stock_fuera_rango',COUNT(*) FROM catalog.stock_fisico
  WHERE "CantidadTotal"<0 OR "CantidadReservada"<0 OR "CantidadBloqueada"<0
  OR ("CantidadReservada"+"CantidadBloqueada")>"CantidadTotal"
UNION ALL SELECT 'roles_no_canonicos',COUNT(*) FROM core.usuarios
  WHERE "Rol" NOT IN ('Admin','Vendedor','Comprador');
SELECT 'core' AS modulo,"MigrationId" FROM core."__EFMigrationsHistory"
UNION ALL SELECT 'catalog',"MigrationId" FROM catalog."__EFMigrationsHistory"
UNION ALL SELECT 'finance',"MigrationId" FROM finance."__EFMigrationsHistory"
ORDER BY modulo,"MigrationId";
