using System;
using MarketplaceERP.Finance.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketplaceERP.Finance.Migrations;

/// <summary>
/// SOLO SANDBOX QA: prototipo de migración aditiva para 1 intento → N reservas.
/// NO está integrado en PR ni autorizado para producción.
/// Conserva la columna legacy finance.intentos_pago.ReservaId.
/// </summary>
[DbContext(typeof(FinanceDbContext))]
[Migration("20261108060000_QaMultiSkuIntentoReservas")]
public sealed class QaMultiSkuIntentoReservas : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Barrera complementaria por NOMBRE de BD temporal de tests.
        // No verifica el host: los servicios QA verifican localhost; por eso
        // esta migración nunca debe copiarse ni desplegarse en producción.
        migrationBuilder.Sql("""
            DO $qa_guard$
            BEGIN
                IF NOT (
                    left(current_database(), 3) = 'ci_' OR
                    left(current_database(), 8) = 'p0_flow_' OR
                    left(current_database(), 11) = 'p1_history_'
                ) THEN
                    RAISE EXCEPTION
                        'QA_MIGRATION_ONLY: nombre de base no coincide con bases efimeras de pruebas';
                END IF;
            END
            $qa_guard$;
            """);

        migrationBuilder.CreateTable(
            name: "intento_reservas_pago",
            schema: "finance",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                IntentoPagoId = table.Column<Guid>(type: "uuid", nullable: false),
                ReservaId = table.Column<Guid>(type: "uuid", nullable: false),
                ProductoId = table.Column<Guid>(type: "uuid", nullable: false),
                Cantidad = table.Column<int>(type: "integer", nullable: false),
                Procedencia = table.Column<string>(type: "character varying(32)",
                    maxLength: 32, nullable: false),
                CreadoUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_intento_reservas_pago", x => x.Id);
                table.ForeignKey(
                    name: "FK_intento_reservas_pago_intentos_pago_IntentoPagoId",
                    column: x => x.IntentoPagoId,
                    principalSchema: "finance", principalTable: "intentos_pago",
                    principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                table.CheckConstraint("CK_intento_reservas_pago_cantidad", "\"Cantidad\" > 0");
            });

        migrationBuilder.CreateIndex(
            name: "IX_intento_reservas_pago_IntentoPagoId_ProductoId",
            schema: "finance", table: "intento_reservas_pago",
            columns: new[] { "IntentoPagoId", "ProductoId" }, unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_intento_reservas_pago_ReservaId",
            schema: "finance", table: "intento_reservas_pago",
            column: "ReservaId", unique: true);

        // Backfill conservador: SOLO una línea comercial por orden.
        // No declarar como verificada una reserva histórica, ni adivinar la
        // reserva ausente de las órdenes históricas con varios SKU.
        // Los casos ambiguos permanecen en la columna legacy para revisión.
        // Si se reutilizó indebidamente la MISMA ReservaId en varias órdenes,
        // el índice UNIQUE hace fallar la migración; NO omitir ni ocultar datos.
        migrationBuilder.Sql("""
            INSERT INTO finance.intento_reservas_pago
                ("Id", "IntentoPagoId", "ReservaId", "ProductoId",
                 "Cantidad", "Procedencia", "CreadoUtc")
            SELECT i."Id", i."Id", i."ReservaId",
                   d."ProductoId", d."Cantidad",
                   'LegacySinVerificar', i."CreadoUtc"
            FROM finance.intentos_pago i
            JOIN finance.items_orden d ON d."OrdenId" = i."OrdenId"
            WHERE i."ReservaId" IS NOT NULL
              AND i."ReservaId" <> '00000000-0000-0000-0000-000000000000'::uuid
              AND d."Cantidad" > 0
              AND d."ProductoId" <> '00000000-0000-0000-0000-000000000000'::uuid
              AND NOT EXISTS (
                  SELECT 1 FROM finance.items_orden otro
                  WHERE otro."OrdenId" = d."OrdenId" AND otro."Id" <> d."Id"
              );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new InvalidOperationException(
            "No automatizar DROP de la tabla durable de reservas de pago: " +
            "requiere plan explícito de rollback y preservación de datos.");
    }
}
