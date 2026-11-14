using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketplaceERP.Finance.Migrations
{
    /// <inheritdoc />
    public partial class FIN09LiquidacionUnoAMuchosExpandir : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No alterar historicos inconsistentes: bloquear migracion ANTES del
            // backfill, conservando el esquema y las filas previas.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM finance.liquidaciones l
                        JOIN finance.splits_pago s ON s."Id" = l."SplitPagoId"
                        JOIN finance.ordenes o ON o."Id" = s."OrdenId"
                        WHERE l."VendedorId" <> o."VendedorId"
                           OR l."MontoLiquidar" <> s."MontoVendedor"
                           OR s."MontoVendedor" <= 0
                           OR l."Moneda" <> o."Moneda"
                    ) THEN
                        RAISE EXCEPTION
                          'FIN09: Liquidaciones historicas inconsistentes; conciliar antes de migrar.'
                          USING ERRCODE = '23514';
                    END IF;
                END $$;
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "LiquidacionId",
                schema: "finance",
                table: "splits_pago",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_splits_pago_LiquidacionId",
                schema: "finance",
                table: "splits_pago",
                column: "LiquidacionId");

            migrationBuilder.AddForeignKey(
                name: "FK_splits_pago_liquidaciones_LiquidacionId",
                schema: "finance",
                table: "splits_pago",
                column: "LiquidacionId",
                principalSchema: "finance",
                principalTable: "liquidaciones",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Enlace historico 1:1 -> FK 1:N, sin borrar ni cambiar importes.
            migrationBuilder.Sql("""
                UPDATE finance.splits_pago AS s
                SET "LiquidacionId" = l."Id"
                FROM finance.liquidaciones AS l
                WHERE l."SplitPagoId" = s."Id"
                  AND s."LiquidacionId" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Contraer 1:N a 1:1 pierde enlaces secundarios; fail-closed.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT "LiquidacionId"
                        FROM finance.splits_pago
                        WHERE "LiquidacionId" IS NOT NULL
                        GROUP BY "LiquidacionId"
                        HAVING COUNT(*) > 1
                    ) OR EXISTS (
                        SELECT 1
                        FROM finance.splits_pago s
                        JOIN finance.liquidaciones l ON l."Id" = s."LiquidacionId"
                        WHERE s."Id" <> l."SplitPagoId"
                    ) THEN
                        RAISE EXCEPTION
                          'FIN09: No se puede revertir un lote con varios splits; snapshot/forward-fix.'
                          USING ERRCODE = '23514';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_splits_pago_liquidaciones_LiquidacionId",
                schema: "finance",
                table: "splits_pago");

            migrationBuilder.DropIndex(
                name: "IX_splits_pago_LiquidacionId",
                schema: "finance",
                table: "splits_pago");

            migrationBuilder.DropColumn(
                name: "LiquidacionId",
                schema: "finance",
                table: "splits_pago");
        }
    }
}
