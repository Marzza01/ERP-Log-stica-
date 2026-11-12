using Npgsql;

namespace MarketplaceERP.API.Services;

/// <summary>
/// F17 sandbox prototype. Checks known incompatibilities before ANY module
/// migration is applied. The caller owns an acquired global advisory lock.
/// This does NOT make a multi-DbContext migration globally transactional.
/// </summary>
internal static class MigrationGlobalPreflight
{
    public static async Task ValidateAsync(NpgsqlConnection lockedConnection)
    {
        if (lockedConnection.State != System.Data.ConnectionState.Open)
            throw new InvalidOperationException("Global preflight requires the open locked connection.");

        const string sql = """
            DO $gate65$
            BEGIN
                -- Reject QA migration IDs in BOTH possible EF history locations.
                IF to_regclass('finance."__EFMigrationsHistory"') IS NOT NULL THEN
                    IF EXISTS (
                        SELECT 1 FROM finance."__EFMigrationsHistory"
                        WHERE "MigrationId" IN (
                            '20261108060000_QaMultiSkuIntentoReservas',
                            '20261109203804_Qa09CancelacionPreEnvioAuditable')
                    ) THEN
                        RAISE EXCEPTION 'GATE65_QA_FINANCE_HISTORY_PRESENT';
                    END IF;
                END IF;
                IF to_regclass('public."__EFMigrationsHistory"') IS NOT NULL THEN
                    IF EXISTS (
                        SELECT 1 FROM public."__EFMigrationsHistory"
                        WHERE "MigrationId" IN (
                            '20261108060000_QaMultiSkuIntentoReservas',
                            '20261109203804_Qa09CancelacionPreEnvioAuditable')
                    ) THEN
                        RAISE EXCEPTION 'GATE65_QA_PUBLIC_HISTORY_PRESENT';
                    END IF;
                END IF;

                -- Catalog WMS duplicate source data: no automatic renumbering.
                IF to_regclass('catalog."PickLists"') IS NOT NULL THEN
                    IF EXISTS (
                        SELECT 1 FROM catalog."PickLists"
                        GROUP BY "NumeroPickList" HAVING COUNT(*) > 1
                    ) OR EXISTS (
                        SELECT 1 FROM catalog."PickLists"
                        GROUP BY "OrdenId" HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'GATE65_WMS_DUPLICATE_PICKLIST';
                    END IF;
                END IF;
                IF to_regclass('catalog."PaquetesDespacho"') IS NOT NULL THEN
                    IF EXISTS (
                        SELECT 1 FROM catalog."PaquetesDespacho"
                        GROUP BY "NumeroGuia" HAVING COUNT(*) > 1
                    ) OR EXISTS (
                        SELECT 1 FROM catalog."PaquetesDespacho"
                        GROUP BY "OrdenId" HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'GATE65_WMS_DUPLICATE_PACKAGE';
                    END IF;
                END IF;

                -- FIN08: no duplicate automatic accounting postings.
                IF to_regclass('finance.partidas_contables') IS NOT NULL THEN
                    IF EXISTS (
                        SELECT 1 FROM finance.partidas_contables
                        WHERE "TipoOperacion" IN ('Venta', 'Liquidacion')
                          AND "ReferenciaOrigenId" IS NOT NULL
                        GROUP BY "TipoOperacion", "ReferenciaOrigenId"
                        HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'GATE65_FIN08_DUPLICATE_LEDGER';
                    END IF;
                END IF;

                -- Checkout: reject any historical reservation used by multiple attempts.
                IF to_regclass('finance.intentos_pago') IS NOT NULL THEN
                    IF EXISTS (
                        SELECT 1 FROM finance.intentos_pago
                        WHERE "ReservaId" IS NOT NULL
                          AND "ReservaId" <> '00000000-0000-0000-0000-000000000000'::uuid
                        GROUP BY "ReservaId" HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'GATE65_DUPLICATE_LEGACY_RESERVA';
                    END IF;
                END IF;

                -- FIN09: schema-aware guard, important for genuine 1:N settlements.
                -- BEFORE FIN09: legacy amount must equal its sole anchor split.
                -- AFTER FIN09: anchor remains, but amount must equal SUM of
                -- all linked splits; it cannot equal the first split alone.
                IF to_regclass('finance.liquidaciones') IS NOT NULL
                   AND to_regclass('finance.splits_pago') IS NOT NULL
                   AND to_regclass('finance.ordenes') IS NOT NULL THEN
                    IF to_regclass('finance."__EFMigrationsHistory"') IS NOT NULL
                       AND EXISTS (
                           SELECT 1 FROM finance."__EFMigrationsHistory"
                           WHERE "MigrationId" =
                             '20261110050932_FIN09LiquidacionUnoAMuchosExpandir'
                       ) THEN
                        IF NOT EXISTS (
                            SELECT 1 FROM information_schema.columns
                            WHERE table_schema = 'finance'
                              AND table_name = 'splits_pago'
                              AND column_name = 'LiquidacionId'
                        ) THEN
                            RAISE EXCEPTION 'GATE65_FIN09_HISTORY_SCHEMA_DRIFT';
                        END IF;
                        -- Every old 1:1 anchor must be a member of the 1:N group.
                        IF EXISTS (
                            SELECT 1 FROM finance.liquidaciones l
                            WHERE NOT EXISTS (
                                SELECT 1 FROM finance.splits_pago s
                                WHERE s."Id" = l."SplitPagoId"
                                  AND s."LiquidacionId" = l."Id"
                            )
                        ) THEN
                            RAISE EXCEPTION 'GATE65_FIN09_GROUP_INCONSISTENT';
                        END IF;
                        -- Each linked split belongs to the same seller/currency
                        -- and has a positive amount.
                        IF EXISTS (
                            SELECT 1 FROM finance.splits_pago s
                            JOIN finance.liquidaciones l ON l."Id" = s."LiquidacionId"
                            JOIN finance.ordenes o ON o."Id" = s."OrdenId"
                            WHERE s."LiquidacionId" IS NOT NULL
                              AND (l."VendedorId" <> o."VendedorId"
                                   OR l."Moneda" <> o."Moneda"
                                   OR s."MontoVendedor" <= 0)
                        ) THEN
                            RAISE EXCEPTION 'GATE65_FIN09_GROUP_INCONSISTENT';
                        END IF;
                        -- The service's actual 1:N invariant: settlement total
                        -- must equal all of its linked seller portions.
                        IF EXISTS (
                            SELECT 1
                            FROM finance.liquidaciones l
                            LEFT JOIN (
                                SELECT s."LiquidacionId" AS settlement_id,
                                       SUM(s."MontoVendedor") AS total
                                FROM finance.splits_pago s
                                WHERE s."LiquidacionId" IS NOT NULL
                                GROUP BY s."LiquidacionId"
                            ) g ON g.settlement_id = l."Id"
                            WHERE g.total IS NULL OR l."MontoLiquidar" <> g.total
                        ) THEN
                            RAISE EXCEPTION 'GATE65_FIN09_GROUP_INCONSISTENT';
                        END IF;
                    ELSE
                        IF EXISTS (
                            SELECT 1 FROM finance.liquidaciones l
                            JOIN finance.splits_pago s ON s."Id" = l."SplitPagoId"
                            JOIN finance.ordenes o ON o."Id" = s."OrdenId"
                            WHERE l."VendedorId" <> o."VendedorId"
                               OR l."MontoLiquidar" <> s."MontoVendedor"
                               OR s."MontoVendedor" <= 0
                               OR l."Moneda" <> o."Moneda"
                        ) THEN
                            RAISE EXCEPTION 'GATE65_FIN09_INCONSISTENT_HISTORY';
                        END IF;
                    END IF;
                END IF;

                -- Existing QA-only table is always a manual review blocker.
                IF to_regclass('finance.cancelaciones_pre_envio_qa') IS NOT NULL THEN
                    RAISE EXCEPTION 'GATE65_QA_AUDIT_TABLE_PRESENT';
                END IF;

                -- Detect schema changes present without corresponding EF history.
                IF to_regclass('finance."__EFMigrationsHistory"') IS NOT NULL THEN
                    IF to_regclass('finance.intento_reservas_pago') IS NOT NULL
                        AND NOT EXISTS (
                            SELECT 1 FROM finance."__EFMigrationsHistory"
                            WHERE "MigrationId" = '20261108060000_IntentoReservasMultiSku'
                        ) THEN
                        RAISE EXCEPTION 'GATE65_MULTI_SKU_TABLE_WITHOUT_HISTORY';
                    END IF;
                    IF to_regclass('finance.cancelaciones_pre_envio') IS NOT NULL
                        AND NOT EXISTS (
                            SELECT 1 FROM finance."__EFMigrationsHistory"
                            WHERE "MigrationId" = '20261109203804_CancelacionPreEnvioAuditada'
                        ) THEN
                        RAISE EXCEPTION 'GATE65_CANCEL_TABLE_WITHOUT_HISTORY';
                    END IF;
                END IF;
                IF to_regclass('catalog."__EFMigrationsHistory"') IS NOT NULL THEN
                    IF EXISTS (
                        SELECT 1 FROM pg_indexes
                        WHERE schemaname = 'catalog'
                          AND indexname = 'UX_WMS_PickList_NumeroPickList'
                    ) AND NOT EXISTS (
                        SELECT 1 FROM catalog."__EFMigrationsHistory"
                        WHERE "MigrationId" = '20261110013000_WmsReferenciasExternasUnicas'
                    ) THEN
                        RAISE EXCEPTION 'GATE65_WMS_INDEX_WITHOUT_HISTORY';
                    END IF;
                END IF;
            END $gate65$;
            """;

        await using var command = lockedConnection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 30;
        await command.ExecuteNonQueryAsync();
    }
}
