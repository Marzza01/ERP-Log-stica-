using System;
using MarketplaceERP.Finance.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketplaceERP.Finance.Migrations;

/// <summary>
/// Migracion de producto ADITIVA para auditoria persistente de cancelaciones
/// previas al envio. No autoriza cancelar cobros ni sustituye conciliacion
/// bancaria. La ruta de QA que escribe este evento sigue excluida de Release.
/// </summary>
[DbContext(typeof(FinanceDbContext))]
[Migration("20261109203804_CancelacionPreEnvioAuditada")]
public sealed class CancelacionPreEnvioAuditada : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $preflight$
            DECLARE qa_history boolean := false;
            BEGIN
                IF to_regclass('finance."__EFMigrationsHistory"') IS NOT NULL THEN
                    SELECT EXISTS (
                        SELECT 1 FROM finance."__EFMigrationsHistory"
                        WHERE "MigrationId" = '20261109203804_Qa09CancelacionPreEnvioAuditable'
                    ) INTO qa_history;
                END IF;
                IF to_regclass('public."__EFMigrationsHistory"') IS NOT NULL THEN
                    SELECT qa_history OR EXISTS (
                        SELECT 1 FROM public."__EFMigrationsHistory"
                        WHERE "MigrationId" = '20261109203804_Qa09CancelacionPreEnvioAuditable'
                    ) INTO qa_history;
                END IF;

                -- No reinterpretar ni sobreescribir auditorias QA preexistentes.
                IF qa_history OR
                   to_regclass('finance.cancelaciones_pre_envio_qa') IS NOT NULL THEN
                    RAISE EXCEPTION
                        'CANCELACION_AUDITORIA_QA_EXISTENTE: revisar y conservar registros antes de migrar';
                END IF;
                IF to_regclass('finance.cancelaciones_pre_envio') IS NOT NULL THEN
                    RAISE EXCEPTION
                        'CANCELACION_AUDITORIA_TABLA_EXISTENTE: revisar antes de migrar';
                END IF;
            END $preflight$;
            """);

        migrationBuilder.CreateTable(
            name: "cancelaciones_pre_envio",
            schema: "finance",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrdenId = table.Column<Guid>(type: "uuid", nullable: false),
                IntentoPagoId = table.Column<Guid>(type: "uuid", nullable: false),
                OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                Motivo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ReservasLiberadas = table.Column<int>(type: "integer", nullable: false),
                RegistradoUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_cancelaciones_pre_envio", x => x.Id);
                table.CheckConstraint("CK_cancelaciones_pre_envio_reservas",
                    "\"ReservasLiberadas\" >= 0");
                table.ForeignKey(
                    name: "FK_cancelaciones_pre_envio_intentos_pago_IntentoPagoId",
                    column: x => x.IntentoPagoId,
                    principalSchema: "finance", principalTable: "intentos_pago",
                    principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_cancelaciones_pre_envio_ordenes_OrdenId",
                    column: x => x.OrdenId,
                    principalSchema: "finance", principalTable: "ordenes",
                    principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_cancelaciones_pre_envio_IntentoPagoId",
            schema: "finance", table: "cancelaciones_pre_envio",
            column: "IntentoPagoId", unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_cancelaciones_pre_envio_OrdenId",
            schema: "finance", table: "cancelaciones_pre_envio",
            column: "OrdenId", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // La auditoria es evidencia: nunca hacer DROP en rollback automatizado,
        // incluso si en el instante actual estuviera vacia.
        throw new InvalidOperationException(
            "CANCELACION_AUDITORIA_DOWN_BLOQUEADO: conservar eventos y usar snapshot " +
            "verificado o forward-fix bajo control de cambios.");
    }
}
