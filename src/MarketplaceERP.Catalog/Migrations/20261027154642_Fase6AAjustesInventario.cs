using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketplaceERP.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class Fase6AAjustesInventario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ajustes_inventario",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StockFisicoId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Cantidad = table.Column<int>(type: "integer", nullable: false),
                    Motivo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Detalle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Referencia = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreadoPor = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    AprobadoPor = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    AplicadoPor = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    FechaCreacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    FechaDecision = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FechaAplicacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ajustes_inventario", x => x.Id);
                    table.CheckConstraint("CK_ajustes_inventario_cantidad_positiva", "\"Cantidad\" > 0");
                    table.ForeignKey(
                        name: "FK_ajustes_inventario_productos_ProductoId",
                        column: x => x.ProductoId,
                        principalSchema: "catalog",
                        principalTable: "productos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ajustes_inventario_stock_fisico_StockFisicoId",
                        column: x => x.StockFisicoId,
                        principalSchema: "catalog",
                        principalTable: "stock_fisico",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ajustes_inventario_Estado",
                schema: "catalog",
                table: "ajustes_inventario",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_ajustes_inventario_FechaCreacion",
                schema: "catalog",
                table: "ajustes_inventario",
                column: "FechaCreacion");

            migrationBuilder.CreateIndex(
                name: "IX_ajustes_inventario_ProductoId",
                schema: "catalog",
                table: "ajustes_inventario",
                column: "ProductoId");

            migrationBuilder.CreateIndex(
                name: "IX_ajustes_inventario_StockFisicoId",
                schema: "catalog",
                table: "ajustes_inventario",
                column: "StockFisicoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ajustes_inventario",
                schema: "catalog");
        }
    }
}
