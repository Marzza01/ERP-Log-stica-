using System;
using MarketplaceERP.Finance.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketplaceERP.Finance.Migrations;

/// <summary>
/// Migración de DOMINIO propuesta para un intento de pago con N reservas.
/// NO ES UN PERMISO DE DESPLIEGUE: solo se puede aplicar después de snapshot
/// verificado, control de escritores y preflight sobre el destino autorizado.
/// Mantiene finance.intentos_pago.ReservaId intacto para compatibilidad legacy.
/// </summary>
[DbContext(typeof(FinanceDbContext))]
[Migration("20261108060000_IntentoReservasMultiSku")]
public sealed class IntentoReservasMultiSku : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Nunca reinterpretar una migración QA previa como migración comercial.
        // Un historial QA necesita conciliación/manual, no un CREATE duplicado.
        migrationBuilder.Sql("""
            DO $preflight$
            DECLARE
                qa_historial boolean := false;
            BEGIN
                -- La API mantiene el historial en finance; clientes EF antiguos
                -- y fixtures usan public. Nunca consultar una tabla inexistente.
                IF to_regclass('finance."__EFMigrationsHistory"') IS NULL
                   AND to_regclass('public."__EFMigrationsHistory"') IS NULL THEN
                    RAISE EXCEPTION 'CHECKOUT_MIGRATION_HISTORY_NOT_FOUND';
                END IF;

                -- No usar ELSIF: pueden coexistir ambos historiales.
                -- Cualquier migracion QA previa en UNO de ellos bloquea
                -- el upgrade comercial, sin sobrescribir ni borrar datos.
                IF to_regclass('finance."__EFMigrationsHistory"') IS NOT NULL THEN
                    SELECT EXISTS (
                        SELECT 1 FROM finance."__EFMigrationsHistory"
                        WHERE "MigrationId" = '20261108060000_QaMultiSkuIntentoReservas'
                    ) INTO qa_historial;
                END IF;
                IF to_regclass('public."__EFMigrationsHistory"') IS NOT NULL THEN
                    SELECT qa_historial OR EXISTS (
                        SELECT 1 FROM public."__EFMigrationsHistory"
                        WHERE "MigrationId" = '20261108060000_QaMultiSkuIntentoReservas'
                    ) INTO qa_historial;
                END IF;
                IF qa_historial THEN
                    RAISE EXCEPTION 'CHECKOUT_MIGRATION_CONFLICT_QA_HISTORY: revisar antes de continuar';
                END IF;

                -- Verificar toda reserva historica, incluso si la orden
                -- es multi-SKU o carece de items validos para el backfill.
                -- Una reserva repetida entre intentos NO puede aceptarse
                -- por quedar fuera del subconjunto de reconstruccion 1:1.
                IF EXISTS (
                    SELECT 1
                    FROM finance.intentos_pago i
                    WHERE i."ReservaId" IS NOT NULL
                      AND i."ReservaId" <> '00000000-0000-0000-0000-000000000000'::uuid
                    GROUP BY i."ReservaId"
                    HAVING COUNT(*) > 1
                ) THEN
                    RAISE EXCEPTION 'CHECKOUT_LEGACY_RESERVA_DUPLICADA: conciliacion manual requerida';
                END IF;
            END
            $preflight$;
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

        // Backfill 1:1 SOLO si una orden tiene una línea comercial.
        // La procedencia histórica sigue marcada como no verificada.
        // Los pedidos multisku ambiguos no se reconstruyen artificialmente.
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
            "CHECKOUT_MULTI_SKU_DOWN_BLOQUEADO: no destruir referencias de reservas " +
            "ni revertir sin snapshot verificado y un plan de recuperacion aprobado.");
    }
}
