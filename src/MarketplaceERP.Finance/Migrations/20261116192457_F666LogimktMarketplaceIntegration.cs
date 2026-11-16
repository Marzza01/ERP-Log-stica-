using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketplaceERP.Finance.Migrations
{
    /// <inheritdoc />
    public partial class F666LogimktMarketplaceIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "marketplace_order_metadata",
                schema: "finance",
                columns: table => new
                {
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalOrderCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CustomerName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CustomerEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    CustomerPhone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Company = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    TaxId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    RequestedPaymentMethod = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PaymentStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    ExternalReturnCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ReturnReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReturnRmaIdsJson = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ReturnStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ReturnRequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketplace_order_metadata", x => x.OrderId);
                    table.ForeignKey(
                        name: "FK_marketplace_order_metadata_ordenes_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "finance",
                        principalTable: "ordenes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_marketplace_order_metadata_ExternalOrderCode",
                schema: "finance",
                table: "marketplace_order_metadata",
                column: "ExternalOrderCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketplace_order_metadata_ExternalReturnCode",
                schema: "finance",
                table: "marketplace_order_metadata",
                column: "ExternalReturnCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "marketplace_order_metadata",
                schema: "finance");
        }
    }
}
