using System;
using MarketplaceERP.Finance.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketplaceERP.Finance.Migrations;

/// <summary>
/// FIN-07: no inventa líneas para facturas históricas. Se conservan los
/// documentos antiguos, que deberán documentarse antes de autorizarse.
/// </summary>
[DbContext(typeof(FinanceDbContext))]
[Migration("20261107022000_FIN07DetalleFacturaProveedor")]
public sealed class FIN07DetalleFacturaProveedor : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "items_factura_proveedor",
            schema: "finance",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                FacturaProveedorId = table.Column<Guid>(type: "uuid", nullable: false),
                ItemOrdenCompraId = table.Column<Guid>(type: "uuid", nullable: false),
                CantidadFacturada = table.Column<int>(type: "integer", nullable: false),
                PrecioUnitarioFacturado = table.Column<decimal>(
                    type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_items_factura_proveedor", x => x.Id);
                table.ForeignKey(
                    name: "FK_items_factura_proveedor_facturas_proveedor_FacturaProveedorId",
                    column: x => x.FacturaProveedorId,
                    principalSchema: "finance",
                    principalTable: "facturas_proveedor",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_items_factura_proveedor_items_orden_compra_ItemOrdenCompraId",
                    column: x => x.ItemOrdenCompraId,
                    principalSchema: "finance",
                    principalTable: "items_orden_compra",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.CheckConstraint("CK_FIN07_factura_linea_cantidad", "\"CantidadFacturada\" > 0");
                table.CheckConstraint("CK_FIN07_factura_linea_precio", "\"PrecioUnitarioFacturado\" > 0");
            });

        migrationBuilder.CreateIndex(
            name: "IX_items_factura_proveedor_FacturaProveedorId_ItemOrdenCompraId",
            schema: "finance",
            table: "items_factura_proveedor",
            columns: new[] { "FacturaProveedorId", "ItemOrdenCompraId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_items_factura_proveedor_ItemOrdenCompraId",
            schema: "finance",
            table: "items_factura_proveedor",
            column: "ItemOrdenCompraId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "items_factura_proveedor", schema: "finance");
    }
}
