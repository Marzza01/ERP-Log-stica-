using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketplaceERP.Finance.Migrations
{
    /// <inheritdoc />
    public partial class LiquidacionUnoAMuchosSplits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LiquidacionId",
                schema: "finance",
                table: "splits_pago",
                type: "uuid",
                nullable: true);

            // Migración de datos: para cada liquidación existente, asignar su Id al nuevo LiquidacionId del split
            migrationBuilder.Sql(@"
                UPDATE finance.splits_pago s
                SET ""LiquidacionId"" = l.""Id""
                FROM finance.liquidaciones l
                WHERE l.""SplitPagoId"" = s.""Id"";
            ");

            migrationBuilder.DropForeignKey(
                name: "FK_liquidaciones_splits_pago_SplitPagoId",
                schema: "finance",
                table: "liquidaciones");

            migrationBuilder.DropIndex(
                name: "IX_liquidaciones_SplitPagoId",
                schema: "finance",
                table: "liquidaciones");

            migrationBuilder.DropColumn(
                name: "SplitPagoId",
                schema: "finance",
                table: "liquidaciones");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "finance",
                table: "liquidaciones",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

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
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "finance",
                table: "liquidaciones");

            migrationBuilder.AddColumn<Guid>(
                name: "SplitPagoId",
                schema: "finance",
                table: "liquidaciones",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_liquidaciones_SplitPagoId",
                schema: "finance",
                table: "liquidaciones",
                column: "SplitPagoId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_liquidaciones_splits_pago_SplitPagoId",
                schema: "finance",
                table: "liquidaciones",
                column: "SplitPagoId",
                principalSchema: "finance",
                principalTable: "splits_pago",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
