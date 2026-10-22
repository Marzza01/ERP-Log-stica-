using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketplaceERP.Finance.Migrations
{
    /// <inheritdoc />
    public partial class ContabilidadSecuenciaEIdempotencia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_splits_pago_liquidaciones_LiquidacionId",
                schema: "finance",
                table: "splits_pago");

            migrationBuilder.CreateSequence(
                name: "PartidaContableNumeroSeq",
                schema: "finance");

            migrationBuilder.CreateIndex(
                name: "IX_partidas_contables_TipoOperacion_ReferenciaOrigenId",
                schema: "finance",
                table: "partidas_contables",
                columns: new[] { "TipoOperacion", "ReferenciaOrigenId" },
                unique: true,
                filter: "\"ReferenciaOrigenId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_splits_pago_liquidaciones_LiquidacionId",
                schema: "finance",
                table: "splits_pago",
                column: "LiquidacionId",
                principalSchema: "finance",
                principalTable: "liquidaciones",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_splits_pago_liquidaciones_LiquidacionId",
                schema: "finance",
                table: "splits_pago");

            migrationBuilder.DropIndex(
                name: "IX_partidas_contables_TipoOperacion_ReferenciaOrigenId",
                schema: "finance",
                table: "partidas_contables");

            migrationBuilder.DropSequence(
                name: "PartidaContableNumeroSeq",
                schema: "finance");

            migrationBuilder.AddForeignKey(
                name: "FK_splits_pago_liquidaciones_LiquidacionId",
                schema: "finance",
                table: "splits_pago",
                column: "LiquidacionId",
                principalSchema: "finance",
                principalTable: "liquidaciones",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
