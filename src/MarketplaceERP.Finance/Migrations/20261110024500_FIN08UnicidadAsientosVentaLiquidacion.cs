using MarketplaceERP.Finance.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketplaceERP.Finance.Migrations;

/// <summary>
/// FIN-08, SOLO QA aislada: proteccion fisica por operacion contable.
/// El preflight falla cerrado si detecta duplicados historicos. Nunca renumera,
/// elimina ni concilia partidas automaticas.
/// </summary>
[DbContext(typeof(FinanceDbContext))]
[Migration("20261110024500_FIN08UnicidadAsientosVentaLiquidacion")]
public sealed class FIN08UnicidadAsientosVentaLiquidacion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM finance.partidas_contables
                    WHERE "TipoOperacion" IN ('Venta', 'Liquidacion')
                      AND "ReferenciaOrigenId" IS NOT NULL
                    GROUP BY "TipoOperacion", "ReferenciaOrigenId"
                    HAVING COUNT(*) > 1
                ) THEN
                    RAISE EXCEPTION 'FIN08: Asientos historicos duplicados; conciliacion manual obligatoria antes de migrar.'
                        USING ERRCODE = '23505';
                END IF;
            END $$;
            """);

        migrationBuilder.Sql("""
            CREATE UNIQUE INDEX "UX_FIN08_asiento_venta_orden"
            ON finance.partidas_contables ("ReferenciaOrigenId")
            WHERE "TipoOperacion" = 'Venta'
              AND "ReferenciaOrigenId" IS NOT NULL;
            """);

        migrationBuilder.Sql("""
            CREATE UNIQUE INDEX "UX_FIN08_asiento_liquidacion"
            ON finance.partidas_contables ("ReferenciaOrigenId")
            WHERE "TipoOperacion" = 'Liquidacion'
              AND "ReferenciaOrigenId" IS NOT NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "DROP INDEX IF EXISTS finance.\"UX_FIN08_asiento_liquidacion\";");
        migrationBuilder.Sql(
            "DROP INDEX IF EXISTS finance.\"UX_FIN08_asiento_venta_orden\";");
    }
}
