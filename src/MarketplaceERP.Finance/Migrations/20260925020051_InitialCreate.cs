using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketplaceERP.Finance.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "finance");

            migrationBuilder.CreateTable(
                name: "ordenes",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NumeroOrden = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CompradorId = table.Column<Guid>(type: "uuid", nullable: false),
                    VendedorId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    ImpuestoIva = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    CostoEnvio = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    MontoTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "USD"),
                    Estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    TipoCliente = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TipoComprobante = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "FacturaConsumidorFinal"),
                    NumeroComprobanteFiscal = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CondicionPago = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Inmediato"),
                    Transportista = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "LOGIMKT Express"),
                    GuiaEnvio = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ZonaEnvio = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "San Salvador y AMSS"),
                    DireccionEntrega = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    FechaAprobacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FechaEnvio = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FechaEntregaEstimada = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FechaEntregaReal = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FechaCancelacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CumplioOTIF = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ordenes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ordenes_compra",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NumeroPO = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProveedorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProveedorNombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SubTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ImpuestoIva = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    MontoTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "USD"),
                    TerminosPago = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "Crédito 30 días"),
                    Observaciones = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    FechaEmision = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    FechaEntregaEsperada = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FechaEnvioProveedor = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FechaRecepcionReal = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ordenes_compra", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "partidas_contables",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NumeroPartida = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    Concepto = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    TipoOperacion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Venta"),
                    ReferenciaOrigenId = table.Column<Guid>(type: "uuid", nullable: true),
                    DocumentoReferencia = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TotalDebe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalHaber = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    FechaRegistro = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_partidas_contables", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "items_orden",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrdenId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductoId = table.Column<Guid>(type: "uuid", nullable: false),
                    SkuReferencia = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NombreProducto = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Cantidad = table.Column<int>(type: "integer", nullable: false),
                    PrecioUnitario = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_items_orden", x => x.Id);
                    table.ForeignKey(
                        name: "FK_items_orden_ordenes_OrdenId",
                        column: x => x.OrdenId,
                        principalSchema: "finance",
                        principalTable: "ordenes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "splits_pago",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrdenId = table.Column<Guid>(type: "uuid", nullable: false),
                    MontoTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PorcentajeVendedor = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false, defaultValue: 90m),
                    MontoVendedor = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PorcentajeMarketplace = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false, defaultValue: 10m),
                    MontoMarketplace = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TransaccionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FechaProcesado = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_splits_pago", x => x.Id);
                    table.ForeignKey(
                        name: "FK_splits_pago_ordenes_OrdenId",
                        column: x => x.OrdenId,
                        principalSchema: "finance",
                        principalTable: "ordenes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "facturas_proveedor",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NumeroFactura = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    OrdenCompraId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProveedorId = table.Column<Guid>(type: "uuid", nullable: false),
                    MontoSubTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    MontoIva = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    MontoTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    FechaEmision = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    FechaVencimiento = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AprobadaParaPago = table.Column<bool>(type: "boolean", nullable: false),
                    Pagada = table.Column<bool>(type: "boolean", nullable: false),
                    FechaPago = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_facturas_proveedor", x => x.Id);
                    table.ForeignKey(
                        name: "FK_facturas_proveedor_ordenes_compra_OrdenCompraId",
                        column: x => x.OrdenCompraId,
                        principalSchema: "finance",
                        principalTable: "ordenes_compra",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "items_orden_compra",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrdenCompraId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NombreProducto = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CantidadSolicitada = table.Column<int>(type: "integer", nullable: false),
                    CantidadRecibida = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    PrecioUnitarioAcordado = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_items_orden_compra", x => x.Id);
                    table.ForeignKey(
                        name: "FK_items_orden_compra_ordenes_compra_OrdenCompraId",
                        column: x => x.OrdenCompraId,
                        principalSchema: "finance",
                        principalTable: "ordenes_compra",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lineas_partida_contable",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PartidaContableId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodigoCuenta = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    NombreCuenta = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Debe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    Haber = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    Glosa = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lineas_partida_contable", x => x.Id);
                    table.ForeignKey(
                        name: "FK_lineas_partida_contable_partidas_contables_PartidaContableId",
                        column: x => x.PartidaContableId,
                        principalSchema: "finance",
                        principalTable: "partidas_contables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "liquidaciones",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VendedorId = table.Column<Guid>(type: "uuid", nullable: false),
                    SplitPagoId = table.Column<Guid>(type: "uuid", nullable: false),
                    MontoLiquidar = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "USD"),
                    Estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    FechaLiquidacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_liquidaciones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_liquidaciones_splits_pago_SplitPagoId",
                        column: x => x.SplitPagoId,
                        principalSchema: "finance",
                        principalTable: "splits_pago",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "matches_tridireccionales",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CodigoAuditoria = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    OrdenCompraId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacturaProveedorId = table.Column<Guid>(type: "uuid", nullable: true),
                    CantidadOrdenadaPO = table.Column<int>(type: "integer", nullable: false),
                    CantidadRecibidaBodega = table.Column<int>(type: "integer", nullable: false),
                    CantidadFacturada = table.Column<int>(type: "integer", nullable: false),
                    MontoTotalPO = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    MontoTotalFactura = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Resultado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AutorizadoParaPago = table.Column<bool>(type: "boolean", nullable: false),
                    DetalleDiscrepancias = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Auditor = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, defaultValue: "Sistema Automatizado 3-Way Match"),
                    FechaAuditoria = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_matches_tridireccionales", x => x.Id);
                    table.ForeignKey(
                        name: "FK_matches_tridireccionales_facturas_proveedor_FacturaProveedo~",
                        column: x => x.FacturaProveedorId,
                        principalSchema: "finance",
                        principalTable: "facturas_proveedor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_matches_tridireccionales_ordenes_compra_OrdenCompraId",
                        column: x => x.OrdenCompraId,
                        principalSchema: "finance",
                        principalTable: "ordenes_compra",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_facturas_proveedor_NumeroFactura",
                schema: "finance",
                table: "facturas_proveedor",
                column: "NumeroFactura");

            migrationBuilder.CreateIndex(
                name: "IX_facturas_proveedor_OrdenCompraId",
                schema: "finance",
                table: "facturas_proveedor",
                column: "OrdenCompraId");

            migrationBuilder.CreateIndex(
                name: "IX_facturas_proveedor_ProveedorId",
                schema: "finance",
                table: "facturas_proveedor",
                column: "ProveedorId");

            migrationBuilder.CreateIndex(
                name: "IX_items_orden_OrdenId",
                schema: "finance",
                table: "items_orden",
                column: "OrdenId");

            migrationBuilder.CreateIndex(
                name: "IX_items_orden_compra_OrdenCompraId",
                schema: "finance",
                table: "items_orden_compra",
                column: "OrdenCompraId");

            migrationBuilder.CreateIndex(
                name: "IX_lineas_partida_contable_CodigoCuenta",
                schema: "finance",
                table: "lineas_partida_contable",
                column: "CodigoCuenta");

            migrationBuilder.CreateIndex(
                name: "IX_lineas_partida_contable_PartidaContableId",
                schema: "finance",
                table: "lineas_partida_contable",
                column: "PartidaContableId");

            migrationBuilder.CreateIndex(
                name: "IX_liquidaciones_Estado",
                schema: "finance",
                table: "liquidaciones",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_liquidaciones_SplitPagoId",
                schema: "finance",
                table: "liquidaciones",
                column: "SplitPagoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_liquidaciones_VendedorId",
                schema: "finance",
                table: "liquidaciones",
                column: "VendedorId");

            migrationBuilder.CreateIndex(
                name: "IX_matches_tridireccionales_CodigoAuditoria",
                schema: "finance",
                table: "matches_tridireccionales",
                column: "CodigoAuditoria",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_matches_tridireccionales_FacturaProveedorId",
                schema: "finance",
                table: "matches_tridireccionales",
                column: "FacturaProveedorId");

            migrationBuilder.CreateIndex(
                name: "IX_matches_tridireccionales_OrdenCompraId",
                schema: "finance",
                table: "matches_tridireccionales",
                column: "OrdenCompraId");

            migrationBuilder.CreateIndex(
                name: "IX_matches_tridireccionales_Resultado",
                schema: "finance",
                table: "matches_tridireccionales",
                column: "Resultado");

            migrationBuilder.CreateIndex(
                name: "IX_ordenes_CompradorId",
                schema: "finance",
                table: "ordenes",
                column: "CompradorId");

            migrationBuilder.CreateIndex(
                name: "IX_ordenes_Estado",
                schema: "finance",
                table: "ordenes",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_ordenes_GuiaEnvio",
                schema: "finance",
                table: "ordenes",
                column: "GuiaEnvio");

            migrationBuilder.CreateIndex(
                name: "IX_ordenes_NumeroOrden",
                schema: "finance",
                table: "ordenes",
                column: "NumeroOrden",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ordenes_TipoCliente",
                schema: "finance",
                table: "ordenes",
                column: "TipoCliente");

            migrationBuilder.CreateIndex(
                name: "IX_ordenes_VendedorId",
                schema: "finance",
                table: "ordenes",
                column: "VendedorId");

            migrationBuilder.CreateIndex(
                name: "IX_ordenes_compra_Estado",
                schema: "finance",
                table: "ordenes_compra",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_ordenes_compra_NumeroPO",
                schema: "finance",
                table: "ordenes_compra",
                column: "NumeroPO",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ordenes_compra_ProveedorId",
                schema: "finance",
                table: "ordenes_compra",
                column: "ProveedorId");

            migrationBuilder.CreateIndex(
                name: "IX_partidas_contables_Fecha",
                schema: "finance",
                table: "partidas_contables",
                column: "Fecha");

            migrationBuilder.CreateIndex(
                name: "IX_partidas_contables_NumeroPartida",
                schema: "finance",
                table: "partidas_contables",
                column: "NumeroPartida",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_partidas_contables_TipoOperacion",
                schema: "finance",
                table: "partidas_contables",
                column: "TipoOperacion");

            migrationBuilder.CreateIndex(
                name: "IX_splits_pago_OrdenId",
                schema: "finance",
                table: "splits_pago",
                column: "OrdenId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "items_orden",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "items_orden_compra",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "lineas_partida_contable",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "liquidaciones",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "matches_tridireccionales",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "partidas_contables",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "splits_pago",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "facturas_proveedor",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "ordenes",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "ordenes_compra",
                schema: "finance");
        }
    }
}
