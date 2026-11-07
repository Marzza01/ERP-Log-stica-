using MarketplaceERP.Finance.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketplaceERP.Finance.Migrations;

/// <summary>
/// FIN-06: límites físicos de idempotencia de compras. No elimina ni corrige
/// registros históricos. Si existen duplicados, CREATE UNIQUE INDEX falla:
/// primero deben conciliarse manualmente con respaldo y auditoría.
/// Los índices de expresión/parciales se mantienen por SQL (no en el snapshot).
/// </summary>
[DbContext(typeof(FinanceDbContext))]
[Migration("20261107010000_FIN06IntegridadCompras")]
public sealed class FIN06IntegridadCompras : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Una factura con el mismo número normalizado del MISMO proveedor no
        // debe registrarse dos veces, aunque corresponda a distintas POs.
        migrationBuilder.Sql("""
            CREATE UNIQUE INDEX "UX_FIN06_facturas_proveedor_numero"
            ON finance.facturas_proveedor ("ProveedorId", upper(btrim("NumeroFactura")));
            """);

        // Varias discrepancias por PO pueden coexistir, pero como máximo una
        // autorización de pago por orden.
        migrationBuilder.Sql("""
            CREATE UNIQUE INDEX "UX_FIN06_match_po_autorizado"
            ON finance.matches_tridireccionales ("OrdenCompraId")
            WHERE "AutorizadoParaPago" = TRUE;
            """);

        // Como máximo un asiento Compra por PO, incluyendo reintentos y
        // transacciones concurrentes sobre distintos procesos de la API.
        migrationBuilder.Sql("""
            CREATE UNIQUE INDEX "UX_FIN06_asiento_compra_po"
            ON finance.partidas_contables ("ReferenciaOrigenId")
            WHERE "TipoOperacion" = 'Compra' AND "ReferenciaOrigenId" IS NOT NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS finance.\"UX_FIN06_asiento_compra_po\";");
        migrationBuilder.Sql("DROP INDEX IF EXISTS finance.\"UX_FIN06_match_po_autorizado\";");
        migrationBuilder.Sql("DROP INDEX IF EXISTS finance.\"UX_FIN06_facturas_proveedor_numero\";");
    }
}
