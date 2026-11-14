-- MarketplaceERP: auditoría de integridad PostgreSQL (SOLO LECTURA).
-- Ejecutar primero en una BD desechable de CI, nunca asumir que 0 implica cobertura.
-- No contiene INSERT/UPDATE/DELETE/ALTER/CREATE/DROP.
-- No usar para decidir índices UNIQUE sin evaluar multipaquetes y datos productivos.

SELECT
 (SELECT count(*) FROM catalog."PickLists") AS picklists_total,
 (SELECT count(*) FROM catalog."PaquetesDespacho") AS packages_total,
 (SELECT count(*) FROM catalog.devoluciones_rma) AS rma_total,
 (SELECT count(*) FROM finance.partidas_contables WHERE "TipoOperacion" = 'Venta') AS asientos_venta_total,
 (SELECT count(*) FROM finance.ordenes) AS ordenes_cliente_total,
 (SELECT count(*) FROM (SELECT "OrdenId" FROM catalog."PickLists" GROUP BY "OrdenId" HAVING count(*) > 1) s) AS ordenes_con_picklist_duplicada,
 (SELECT count(*) FROM (SELECT "NumeroGuia" FROM catalog."PaquetesDespacho" GROUP BY "NumeroGuia" HAVING count(*) > 1) s) AS guias_duplicadas,
 -- Tener varios paquetes por orden podría ser legítimo; se reporta, no se prohíbe automáticamente.
 (SELECT count(*) FROM (SELECT "OrdenId" FROM catalog."PaquetesDespacho" GROUP BY "OrdenId" HAVING count(*) > 1) s) AS ordenes_con_varios_paquetes,
 (SELECT count(*) FROM (SELECT "ReferenciaOrigenId" FROM finance.partidas_contables
                        WHERE "TipoOperacion" = 'Venta' AND "ReferenciaOrigenId" IS NOT NULL
                        GROUP BY "ReferenciaOrigenId" HAVING count(*) > 1) s) AS ordenes_con_asientos_venta_duplicados,
 (SELECT count(*) FROM catalog.devoluciones_rma r LEFT JOIN finance.ordenes o
                        ON o."Id" = r."OrdenId" WHERE o."Id" IS NULL) AS rma_sin_orden,
 (SELECT count(*) FROM catalog.devoluciones_rma r WHERE NOT EXISTS (
                        SELECT 1 FROM finance.items_orden i
                        WHERE i."OrdenId" = r."OrdenId" AND i."ProductoId" = r."ProductoId")) AS rma_producto_no_vendido,
 (SELECT count(*) FROM catalog.stock_fisico WHERE "UbicacionAlmacenId" IS NULL) AS stock_sin_ubicacion,
 (SELECT count(*) FROM (SELECT "ProductoId" FROM catalog.stock_fisico
                       WHERE "UbicacionAlmacenId" IS NULL GROUP BY "ProductoId" HAVING count(*) > 1) s)
 AS productos_con_saldos_sin_ubicacion_duplicados;

-- Verificar políticas actuales antes de proponer migraciones nuevas.
SELECT schemaname, tablename, indexname, indexdef
FROM pg_indexes
WHERE (schemaname = 'catalog' AND tablename IN ('PickLists', 'PaquetesDespacho', 'stock_fisico'))
   OR (schemaname = 'finance' AND tablename = 'partidas_contables')
ORDER BY schemaname, tablename, indexname;

-- Probar con fixtures no vacíos (>= 2 compradores, >= 2 vendedores,
-- 5 B2B + 5 B2C y solicitudes concurrentes) antes de declarar las reglas verificadas.
