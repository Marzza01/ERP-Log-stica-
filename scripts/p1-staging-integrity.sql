-- P1 / LOGIMKT / AUDITORIA DE LECTURA
-- Aplicar SOLO en Neon staging: proyecto square-lake-18241058,
-- rama br-delicate-bar-bbjeww67, DB marketplaceerp_test.
-- Nunca usar para ejecutar DDL, corregir filas o habilitar pagos.
-- Cada violations debe ser 0; examinar manualmente cualquier desviacion.
-- Las referencias WMS de recepcion incorporan "-ITEM-" + GUID del item sin guiones.
-- Un movimiento sin token ITEM no puede atribuirse a una PO por esta consulta.
WITH checks AS (
    SELECT 'orphan_po_line' AS check_name, COUNT(*) AS violations
      FROM finance.items_orden_compra i
      LEFT JOIN finance.ordenes_compra po ON po."Id" = i."OrdenCompraId"
     WHERE po."Id" IS NULL
    UNION ALL
    SELECT 'orphan_invoice', COUNT(*)
      FROM finance.facturas_proveedor f
      LEFT JOIN finance.ordenes_compra po ON po."Id" = f."OrdenCompraId"
     WHERE po."Id" IS NULL
    UNION ALL
    SELECT 'duplicate_supplier_invoice', COUNT(*)
      FROM (SELECT "NumeroFactura", "ProveedorId"
              FROM finance.facturas_proveedor
             GROUP BY "NumeroFactura", "ProveedorId"
            HAVING COUNT(*) > 1) duplicates
    UNION ALL
    SELECT 'orphan_invoice_line', COUNT(*)
      FROM finance.items_factura_proveedor i
      LEFT JOIN finance.facturas_proveedor f ON f."Id" = i."FacturaProveedorId"
     WHERE f."Id" IS NULL
    UNION ALL
    SELECT 'orphan_liquidation_split', COUNT(*)
      FROM finance.liquidaciones l
      LEFT JOIN finance.splits_pago s ON s."Id" = l."SplitPagoId"
     WHERE s."Id" IS NULL
    UNION ALL
    SELECT 'duplicate_liquidation_split', COUNT(*)
      FROM (SELECT "SplitPagoId"
              FROM finance.liquidaciones
             GROUP BY "SplitPagoId"
            HAVING COUNT(*) > 1) duplicates
    UNION ALL
    SELECT 'liquidation_amount_or_owner_mismatch', COUNT(*)
      FROM finance.liquidaciones l
      JOIN finance.splits_pago s ON s."Id" = l."SplitPagoId"
      JOIN finance.ordenes o ON o."Id" = s."OrdenId"
     WHERE l."MontoLiquidar" <> s."MontoVendedor"
        OR l."VendedorId" <> o."VendedorId"
    UNION ALL
    SELECT 'unbalanced_ledger_entries', COUNT(*)
      FROM finance.partidas_contables p
      LEFT JOIN (SELECT "PartidaContableId",
                        SUM("Debe") AS debe, SUM("Haber") AS haber
                   FROM finance.lineas_partida_contable
                  GROUP BY "PartidaContableId") l
        ON l."PartidaContableId" = p."Id"
     WHERE l."PartidaContableId" IS NULL
        OR p."TotalDebe" <> p."TotalHaber"
        OR p."TotalDebe" <> l.debe
        OR p."TotalHaber" <> l.haber
    UNION ALL
    SELECT 'duplicate_liquidation_journal', COUNT(*)
      FROM (SELECT "ReferenciaOrigenId"
              FROM finance.partidas_contables
             WHERE "TipoOperacion" = 'Liquidacion'
             GROUP BY "ReferenciaOrigenId"
            HAVING COUNT(*) > 1) duplicates
    UNION ALL
    SELECT 'po_received_wms_mismatch', COUNT(*)
      FROM finance.items_orden_compra i
      LEFT JOIN (
          SELECT substring("Referencia" FROM 'ITEM-([a-f0-9]{32})') AS item_key,
                 SUM("Cantidad") AS unidades
            FROM catalog.movimientos_inventario
           WHERE "TipoMovimiento" = 'RecepcionInbound'
           GROUP BY substring("Referencia" FROM 'ITEM-([a-f0-9]{32})')
      ) m ON m.item_key = REPLACE(i."Id"::text, '-', '')
     WHERE i."CantidadRecibida" <> COALESCE(m.unidades, 0)
)
SELECT check_name, violations FROM checks ORDER BY check_name;

-- Datos de contexto: conteos no necesariamente cero.
SELECT 'core' AS esquema, COUNT(*) AS migraciones FROM core."__EFMigrationsHistory"
UNION ALL SELECT 'catalog', COUNT(*) FROM catalog."__EFMigrationsHistory"
UNION ALL SELECT 'finance', COUNT(*) FROM finance."__EFMigrationsHistory"
ORDER BY esquema;

-- No implica desembolso real: pagos permanecen deshabilitados en staging.
SELECT 'liquidaciones' AS tabla, COUNT(*) AS filas FROM finance.liquidaciones
UNION ALL SELECT 'splits_pago', COUNT(*) FROM finance.splits_pago
UNION ALL SELECT 'facturas_proveedor', COUNT(*) FROM finance.facturas_proveedor
UNION ALL SELECT 'partidas_contables', COUNT(*) FROM finance.partidas_contables;

-- Verificaciones de identidad, reservas de stock y conciliacion.
-- Todas deben devolver 0. No emparejar reembolsos o cargos por heuristica.
WITH checks AS (
    SELECT 'checkout_duplicate_idempotency_key' AS check_name, COUNT(*) AS violations
      FROM (SELECT "CompradorId", "IdempotencyKey"
              FROM finance.ordenes WHERE "IdempotencyKey" IS NOT NULL
             GROUP BY "CompradorId", "IdempotencyKey" HAVING COUNT(*) > 1) duplicates
    UNION ALL SELECT 'orphan_payment_attempt', COUNT(*)
      FROM finance.intentos_pago i
      LEFT JOIN finance.ordenes o ON o."Id" = i."OrdenId" WHERE o."Id" IS NULL
    UNION ALL SELECT 'duplicate_payment_attempt_order', COUNT(*)
      FROM (SELECT "OrdenId" FROM finance.intentos_pago
            GROUP BY "OrdenId" HAVING COUNT(*) > 1) duplicates
    UNION ALL SELECT 'orphan_reconciliation_attempt', COUNT(*)
      FROM finance.conciliaciones_pago c
      LEFT JOIN finance.intentos_pago i ON i."Id" = c."IntentoPagoId" WHERE i."Id" IS NULL
    UNION ALL SELECT 'reconciliation_order_mismatch', COUNT(*)
      FROM finance.conciliaciones_pago c
      JOIN finance.intentos_pago i ON i."Id" = c."IntentoPagoId"
     WHERE c."OrdenId" <> i."OrdenId"
    UNION ALL SELECT 'orphan_refund_order', COUNT(*)
      FROM finance.reembolsos_pago r
      LEFT JOIN finance.ordenes o ON o."Id" = r."OrdenId" WHERE o."Id" IS NULL
    UNION ALL SELECT 'refund_duplicate_idempotency', COUNT(*)
      FROM (SELECT "ClaveIdempotencia" FROM finance.reembolsos_pago
            GROUP BY "ClaveIdempotencia" HAVING COUNT(*) > 1) duplicates
    UNION ALL SELECT 'reservation_duplicate_order_product', COUNT(*)
      FROM (SELECT "OrdenId", "ProductoId" FROM catalog.reservas_stock
            GROUP BY "OrdenId", "ProductoId" HAVING COUNT(*) > 1) duplicates
    UNION ALL SELECT 'reservation_orphan_detail', COUNT(*)
      FROM catalog.reservas_stock_detalle d
      LEFT JOIN catalog.reservas_stock r ON r."Id" = d."ReservaStockId" WHERE r."Id" IS NULL
    UNION ALL SELECT 'reservation_detail_sum_mismatch', COUNT(*)
      FROM catalog.reservas_stock r
      LEFT JOIN (SELECT "ReservaStockId", SUM("CantidadAsignada") AS total
                   FROM catalog.reservas_stock_detalle GROUP BY "ReservaStockId") d
        ON d."ReservaStockId" = r."Id"
     WHERE r."CantidadOriginal" <> COALESCE(d.total, 0)
    UNION ALL SELECT 'reservation_exceeds_allocated', COUNT(*)
      FROM catalog.reservas_stock_detalle d
     WHERE d."CantidadAsignada" < 0 OR d."CantidadLiberada" < 0 OR d."CantidadConsumida" < 0
        OR d."CantidadConsumida" + d."CantidadLiberada" > d."CantidadAsignada"
    UNION ALL SELECT 'negative_or_overallocated_physical_stock', COUNT(*)
      FROM catalog.stock_fisico
     WHERE "CantidadTotal" < 0 OR "CantidadReservada" < 0 OR "CantidadBloqueada" < 0
        OR "CantidadReservada" + "CantidadBloqueada" > "CantidadTotal"
)
SELECT check_name, violations FROM checks ORDER BY check_name;
