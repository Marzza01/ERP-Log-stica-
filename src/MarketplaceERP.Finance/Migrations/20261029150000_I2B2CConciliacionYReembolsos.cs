using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketplaceERP.Finance.Migrations
{
    public partial class I2B2CConciliacionYReembolsos : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReservaId", schema: "finance", table: "intentos_pago",
                type: "uuid", nullable: true);

            migrationBuilder.CreateTable(
                name: "conciliaciones_pago", schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IntentoPagoId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrdenId = table.Column<Guid>(type: "uuid", nullable: false),
                    Decision = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    TransaccionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ConsultadoUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conciliaciones_pago", x => x.Id);
                    table.ForeignKey(
                        name: "FK_conciliaciones_pago_intentos_pago_IntentoPagoId",
                        column: x => x.IntentoPagoId,
                        principalSchema: "finance", principalTable: "intentos_pago",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reembolsos_pago", schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrdenId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReservaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaveIdempotencia = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    TransaccionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Monto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Estado = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReembolsoProveedorId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    CreadoUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActualizadoUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reembolsos_pago", x => x.Id);
                    table.ForeignKey(
                        name: "FK_reembolsos_pago_ordenes_OrdenId",
                        column: x => x.OrdenId,
                        principalSchema: "finance", principalTable: "ordenes",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(name: "IX_conciliaciones_pago_IntentoPagoId", schema: "finance",
                table: "conciliaciones_pago", column: "IntentoPagoId");
            migrationBuilder.CreateIndex(name: "IX_conciliaciones_pago_OrdenId_ConsultadoUtc", schema: "finance",
                table: "conciliaciones_pago", columns: new[] { "OrdenId", "ConsultadoUtc" });
            migrationBuilder.CreateIndex(name: "IX_reembolsos_pago_OrdenId", schema: "finance",
                table: "reembolsos_pago", column: "OrdenId", unique: true);
            migrationBuilder.CreateIndex(name: "IX_reembolsos_pago_ClaveIdempotencia", schema: "finance",
                table: "reembolsos_pago", column: "ClaveIdempotencia", unique: true);
            migrationBuilder.CreateIndex(name: "IX_reembolsos_pago_Estado", schema: "finance",
                table: "reembolsos_pago", column: "Estado");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "conciliaciones_pago", schema: "finance");
            migrationBuilder.DropTable(name: "reembolsos_pago", schema: "finance");
            migrationBuilder.DropColumn(name: "ReservaId", schema: "finance", table: "intentos_pago");
        }
    }
}
