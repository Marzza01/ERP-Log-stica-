using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketplaceERP.Finance.Migrations
{
    /// <inheritdoc />
    public partial class I2ACheckoutIdempotencia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CheckoutPayloadHash",
                schema: "finance",
                table: "ordenes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                schema: "finance",
                table: "ordenes",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ordenes_CompradorId_IdempotencyKey",
                schema: "finance",
                table: "ordenes",
                columns: new[] { "CompradorId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ordenes_CompradorId_IdempotencyKey",
                schema: "finance",
                table: "ordenes");

            migrationBuilder.DropColumn(
                name: "CheckoutPayloadHash",
                schema: "finance",
                table: "ordenes");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                schema: "finance",
                table: "ordenes");
        }
    }
}
