using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketplaceERP.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class Fase6BConteosCiclicos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "conteos_ciclicos",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StockFisicoId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CantidadSistema = table.Column<int>(type: "integer", nullable: true),
                    PrimerConteo = table.Column<int>(type: "integer", nullable: true),
                    SegundoConteo = table.Column<int>(type: "integer", nullable: true),
                    AjusteInventarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    Observaciones = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreadoPor = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    IniciadoPor = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    ContadoPor = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    RecontadoPor = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    FechaCreacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    FechaInicio = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FechaPrimerConteo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FechaReconteo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FechaFinalizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conteos_ciclicos", x => x.Id);
                    table.CheckConstraint("CK_conteos_ciclicos_cantidades_no_negativas", "(\"CantidadSistema\" IS NULL OR \"CantidadSistema\" >= 0) AND (\"PrimerConteo\" IS NULL OR \"PrimerConteo\" >= 0) AND (\"SegundoConteo\" IS NULL OR \"SegundoConteo\" >= 0)");
                    table.ForeignKey(
                        name: "FK_conteos_ciclicos_ajustes_inventario_AjusteInventarioId",
                        column: x => x.AjusteInventarioId,
                        principalSchema: "catalog",
                        principalTable: "ajustes_inventario",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_conteos_ciclicos_productos_ProductoId",
                        column: x => x.ProductoId,
                        principalSchema: "catalog",
                        principalTable: "productos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_conteos_ciclicos_stock_fisico_StockFisicoId",
                        column: x => x.StockFisicoId,
                        principalSchema: "catalog",
                        principalTable: "stock_fisico",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_conteos_ciclicos_AjusteInventarioId",
                schema: "catalog",
                table: "conteos_ciclicos",
                column: "AjusteInventarioId");

            migrationBuilder.CreateIndex(
                name: "IX_conteos_ciclicos_Estado",
                schema: "catalog",
                table: "conteos_ciclicos",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_conteos_ciclicos_FechaCreacion",
                schema: "catalog",
                table: "conteos_ciclicos",
                column: "FechaCreacion");

            migrationBuilder.CreateIndex(
                name: "IX_conteos_ciclicos_ProductoId",
                schema: "catalog",
                table: "conteos_ciclicos",
                column: "ProductoId");

            migrationBuilder.CreateIndex(
                name: "IX_conteos_ciclicos_StockFisicoId",
                schema: "catalog",
                table: "conteos_ciclicos",
                column: "StockFisicoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conteos_ciclicos",
                schema: "catalog");
        }
    }
}
