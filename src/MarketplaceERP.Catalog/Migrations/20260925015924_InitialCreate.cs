/**
 * ERP desarrollado por Marcelo Zepeda
 * Todos los derechos reservados © 2026
 */

﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketplaceERP.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.CreateTable(
                name: "PaquetesDespacho",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NumeroGuia = table.Column<string>(type: "text", nullable: false),
                    OrdenId = table.Column<Guid>(type: "uuid", nullable: false),
                    NumeroOrden = table.Column<string>(type: "text", nullable: false),
                    Estado = table.Column<int>(type: "integer", nullable: false),
                    PesoRealKg = table.Column<decimal>(type: "numeric", nullable: false),
                    LargoCm = table.Column<decimal>(type: "numeric", nullable: false),
                    AnchoCm = table.Column<decimal>(type: "numeric", nullable: false),
                    AltoCm = table.Column<decimal>(type: "numeric", nullable: false),
                    Transportista = table.Column<string>(type: "text", nullable: false),
                    DepartamentoDestino = table.Column<string>(type: "text", nullable: false),
                    MunicipioDestino = table.Column<string>(type: "text", nullable: false),
                    DireccionEntrega = table.Column<string>(type: "text", nullable: false),
                    ContactoReceptor = table.Column<string>(type: "text", nullable: false),
                    TelefonoReceptor = table.Column<string>(type: "text", nullable: false),
                    FechaEmpaque = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FechaDespacho = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaquetesDespacho", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PickLists",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NumeroPickList = table.Column<string>(type: "text", nullable: false),
                    OrdenId = table.Column<Guid>(type: "uuid", nullable: false),
                    NumeroOrden = table.Column<string>(type: "text", nullable: false),
                    Estado = table.Column<int>(type: "integer", nullable: false),
                    OperadorAsignado = table.Column<string>(type: "text", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FechaInicio = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FechaFin = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PickLists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "productos",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Nombre = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CategoriaNombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Subcategoria = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PrecioB2C = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PrecioB2B = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "USD"),
                    StockInicial = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    StockMinimo = table.Column<int>(type: "integer", nullable: false, defaultValue: 10),
                    ProveedorNombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PaisOrigen = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "El Salvador"),
                    PesoKg = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 1.0m),
                    DimensionesCm = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    UnidadMedida = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Unidad"),
                    EstadoProducto = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Activo"),
                    CodigoEAN = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Descripcion = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    VendedorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoriaId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstaPublicado = table.Column<bool>(type: "boolean", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    FechaPublicacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_productos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ubicaciones_almacen",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Codigo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Pasillo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Rack = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Nivel = table.Column<int>(type: "integer", nullable: false),
                    Posicion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CapacidadMaxima = table.Column<int>(type: "integer", nullable: false),
                    EstaOcupada = table.Column<bool>(type: "boolean", nullable: false),
                    Zona = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "AlmacenamientoGeneral"),
                    FechaCreacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ubicaciones_almacen", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ItemsPickList",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PickListId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "text", nullable: false),
                    NombreProducto = table.Column<string>(type: "text", nullable: false),
                    CantidadSolicitada = table.Column<int>(type: "integer", nullable: false),
                    CantidadRecogida = table.Column<int>(type: "integer", nullable: false),
                    Pasillo = table.Column<string>(type: "text", nullable: false),
                    Rack = table.Column<string>(type: "text", nullable: false),
                    Nivel = table.Column<int>(type: "integer", nullable: false),
                    Posicion = table.Column<string>(type: "text", nullable: false),
                    UbicacionCodigo = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemsPickList", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemsPickList_PickLists_PickListId",
                        column: x => x.PickListId,
                        principalSchema: "catalog",
                        principalTable: "PickLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "devoluciones_rma",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NumeroRMA = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    OrdenId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Cantidad = table.Column<int>(type: "integer", nullable: false),
                    MotivoCliente = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Clasificacion = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    InspectorTecnico = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    DictamenTecnico = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PorcentajeRemateDescuento = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false, defaultValue: 0m),
                    FechaSolicitud = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    FechaInspeccion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_devoluciones_rma", x => x.Id);
                    table.ForeignKey(
                        name: "FK_devoluciones_rma_productos_ProductoId",
                        column: x => x.ProductoId,
                        principalSchema: "catalog",
                        principalTable: "productos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "movimientos_mwip",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Cantidad = table.Column<int>(type: "integer", nullable: false),
                    TipoEtapa = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NumeroLote = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    UbicacionOrigen = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UbicacionDestino = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "EnProgreso"),
                    Observaciones = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FechaInicio = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    FechaFinalizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_movimientos_mwip", x => x.Id);
                    table.ForeignKey(
                        name: "FK_movimientos_mwip_productos_ProductoId",
                        column: x => x.ProductoId,
                        principalSchema: "catalog",
                        principalTable: "productos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reservas_stock",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductoId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrdenId = table.Column<Guid>(type: "uuid", nullable: false),
                    Cantidad = table.Column<int>(type: "integer", nullable: false),
                    Estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FechaReserva = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    FechaExpiracion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FechaConfirmacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reservas_stock", x => x.Id);
                    table.ForeignKey(
                        name: "FK_reservas_stock_productos_ProductoId",
                        column: x => x.ProductoId,
                        principalSchema: "catalog",
                        principalTable: "productos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_fisico",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductoId = table.Column<Guid>(type: "uuid", nullable: false),
                    CantidadTotal = table.Column<int>(type: "integer", nullable: false),
                    CantidadReservada = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    PuntoReorden = table.Column<int>(type: "integer", nullable: false, defaultValue: 15),
                    UbicacionAlmacenId = table.Column<Guid>(type: "uuid", nullable: true),
                    UbicacionCodigo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "PAS-A-RCK-01-NIV-01-POS-01"),
                    UltimaActualizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_fisico", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stock_fisico_productos_ProductoId",
                        column: x => x.ProductoId,
                        principalSchema: "catalog",
                        principalTable: "productos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_stock_fisico_ubicaciones_almacen_UbicacionAlmacenId",
                        column: x => x.UbicacionAlmacenId,
                        principalSchema: "catalog",
                        principalTable: "ubicaciones_almacen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_devoluciones_rma_NumeroRMA",
                schema: "catalog",
                table: "devoluciones_rma",
                column: "NumeroRMA",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_devoluciones_rma_OrdenId",
                schema: "catalog",
                table: "devoluciones_rma",
                column: "OrdenId");

            migrationBuilder.CreateIndex(
                name: "IX_devoluciones_rma_ProductoId",
                schema: "catalog",
                table: "devoluciones_rma",
                column: "ProductoId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemsPickList_PickListId",
                schema: "catalog",
                table: "ItemsPickList",
                column: "PickListId");

            migrationBuilder.CreateIndex(
                name: "IX_movimientos_mwip_ProductoId",
                schema: "catalog",
                table: "movimientos_mwip",
                column: "ProductoId");

            migrationBuilder.CreateIndex(
                name: "IX_productos_CategoriaId",
                schema: "catalog",
                table: "productos",
                column: "CategoriaId");

            migrationBuilder.CreateIndex(
                name: "IX_productos_CategoriaNombre",
                schema: "catalog",
                table: "productos",
                column: "CategoriaNombre");

            migrationBuilder.CreateIndex(
                name: "IX_productos_CodigoEAN",
                schema: "catalog",
                table: "productos",
                column: "CodigoEAN");

            migrationBuilder.CreateIndex(
                name: "IX_productos_EstaPublicado",
                schema: "catalog",
                table: "productos",
                column: "EstaPublicado");

            migrationBuilder.CreateIndex(
                name: "IX_productos_Sku",
                schema: "catalog",
                table: "productos",
                column: "Sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_productos_VendedorId",
                schema: "catalog",
                table: "productos",
                column: "VendedorId");

            migrationBuilder.CreateIndex(
                name: "IX_reservas_stock_Estado",
                schema: "catalog",
                table: "reservas_stock",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_reservas_stock_OrdenId",
                schema: "catalog",
                table: "reservas_stock",
                column: "OrdenId");

            migrationBuilder.CreateIndex(
                name: "IX_reservas_stock_ProductoId",
                schema: "catalog",
                table: "reservas_stock",
                column: "ProductoId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_fisico_ProductoId",
                schema: "catalog",
                table: "stock_fisico",
                column: "ProductoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_fisico_UbicacionAlmacenId",
                schema: "catalog",
                table: "stock_fisico",
                column: "UbicacionAlmacenId");

            migrationBuilder.CreateIndex(
                name: "IX_ubicaciones_almacen_Codigo",
                schema: "catalog",
                table: "ubicaciones_almacen",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ubicaciones_almacen_Pasillo_Rack_Nivel_Posicion",
                schema: "catalog",
                table: "ubicaciones_almacen",
                columns: new[] { "Pasillo", "Rack", "Nivel", "Posicion" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "devoluciones_rma",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "ItemsPickList",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "movimientos_mwip",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "PaquetesDespacho",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "reservas_stock",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "stock_fisico",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "PickLists",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "productos",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "ubicaciones_almacen",
                schema: "catalog");
        }
    }
}
