using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketplaceERP.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class Fase6CAlertasInventario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "alertas_inventario_revision",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ReconocidaEn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    ReconocidaPor = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alertas_inventario_revision", x => x.Id);
                    table.ForeignKey(
                        name: "FK_alertas_inventario_revision_productos_ProductoId",
                        column: x => x.ProductoId,
                        principalSchema: "catalog",
                        principalTable: "productos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_alertas_inventario_revision_ProductoId_Tipo",
                schema: "catalog",
                table: "alertas_inventario_revision",
                columns: new[] { "ProductoId", "Tipo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_alertas_inventario_revision_ReconocidaEn",
                schema: "catalog",
                table: "alertas_inventario_revision",
                column: "ReconocidaEn");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alertas_inventario_revision",
                schema: "catalog");
        }
    }
}
