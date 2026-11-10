using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketplaceERP.Finance.Migrations
{
    /// <inheritdoc />
    public partial class Qa09CancelacionPreEnvioAuditable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cancelaciones_pre_envio_qa",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrdenId = table.Column<Guid>(type: "uuid", nullable: false),
                    IntentoPagoId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorQaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Motivo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReservasLiberadas = table.Column<int>(type: "integer", nullable: false),
                    RegistradoUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cancelaciones_pre_envio_qa", x => x.Id);
                    table.CheckConstraint("CK_cancelaciones_pre_envio_qa_reservas", "\"ReservasLiberadas\" >= 0");
                    table.ForeignKey(
                        name: "FK_cancelaciones_pre_envio_qa_intentos_pago_IntentoPagoId",
                        column: x => x.IntentoPagoId,
                        principalSchema: "finance",
                        principalTable: "intentos_pago",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cancelaciones_pre_envio_qa_ordenes_OrdenId",
                        column: x => x.OrdenId,
                        principalSchema: "finance",
                        principalTable: "ordenes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cancelaciones_pre_envio_qa_IntentoPagoId",
                schema: "finance",
                table: "cancelaciones_pre_envio_qa",
                column: "IntentoPagoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cancelaciones_pre_envio_qa_OrdenId",
                schema: "finance",
                table: "cancelaciones_pre_envio_qa",
                column: "OrdenId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cancelaciones_pre_envio_qa",
                schema: "finance");
        }
    }
}
