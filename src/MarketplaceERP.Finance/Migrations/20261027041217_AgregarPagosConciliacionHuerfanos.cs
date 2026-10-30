using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketplaceERP.Finance.Migrations
{
    /// <inheritdoc />
    public partial class AgregarPagosConciliacionHuerfanos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pagos_conciliacion_huerfanos",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrdenId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransaccionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Monto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "USD"),
                    MotivoFallo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Reembolsado = table.Column<bool>(type: "boolean", nullable: false),
                    ReembolsoTransaccionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FechaRegistro = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    FechaResolucion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pagos_conciliacion_huerfanos", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pagos_conciliacion_huerfanos_OrdenId",
                schema: "finance",
                table: "pagos_conciliacion_huerfanos",
                column: "OrdenId");

            migrationBuilder.CreateIndex(
                name: "IX_pagos_conciliacion_huerfanos_Reembolsado",
                schema: "finance",
                table: "pagos_conciliacion_huerfanos",
                column: "Reembolsado");

            migrationBuilder.CreateIndex(
                name: "IX_pagos_conciliacion_huerfanos_TransaccionId",
                schema: "finance",
                table: "pagos_conciliacion_huerfanos",
                column: "TransaccionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pagos_conciliacion_huerfanos",
                schema: "finance");
        }
    }
}
