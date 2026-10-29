using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketplaceERP.Finance.Migrations
{
    public partial class I2B2BIntentosPago : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "intentos_pago",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrdenId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferenciaOperacion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Monto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Estado = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TransaccionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreadoUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActualizadoUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_intentos_pago", x => x.Id);
                    table.ForeignKey(
                        name: "FK_intentos_pago_ordenes_OrdenId",
                        column: x => x.OrdenId,
                        principalSchema: "finance",
                        principalTable: "ordenes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_intentos_pago_Estado", schema: "finance", table: "intentos_pago", column: "Estado");
            migrationBuilder.CreateIndex(
                name: "IX_intentos_pago_OrdenId", schema: "finance", table: "intentos_pago", column: "OrdenId", unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_intentos_pago_ReferenciaOperacion", schema: "finance", table: "intentos_pago", column: "ReferenciaOperacion", unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "intentos_pago", schema: "finance");
        }
    }
}
