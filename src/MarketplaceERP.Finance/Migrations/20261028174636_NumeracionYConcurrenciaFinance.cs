using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketplaceERP.Finance.Migrations
{
    /// <inheritdoc />
    public partial class NumeracionYConcurrenciaFinance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "NumeroOrdenSeq",
                schema: "finance");

            migrationBuilder.CreateSequence(
                name: "NumeroPOSeq",
                schema: "finance");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "finance",
                table: "ordenes_compra",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "finance",
                table: "ordenes",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "finance",
                table: "ordenes_compra");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "finance",
                table: "ordenes");

            migrationBuilder.DropSequence(
                name: "NumeroOrdenSeq",
                schema: "finance");

            migrationBuilder.DropSequence(
                name: "NumeroPOSeq",
                schema: "finance");
        }
    }
}
