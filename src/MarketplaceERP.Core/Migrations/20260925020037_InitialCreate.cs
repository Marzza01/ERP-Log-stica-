/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketplaceERP.Core.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "core");

            migrationBuilder.CreateTable(
                name: "categorias",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nombre = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Descripcion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CategoriaPadreId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categorias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_categorias_categorias_CategoriaPadreId",
                        column: x => x.CategoriaPadreId,
                        principalSchema: "core",
                        principalTable: "categorias",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "compradores",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NombreCompleto = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    DireccionEnvio = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    EstaActivo = table.Column<bool>(type: "boolean", nullable: false),
                    FechaRegistro = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_compradores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "proveedores",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NombreComercial = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RazonSocial = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Nit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Nrc = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ContactoPrincipal = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Telefono = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Direccion = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CategoriaSuministro = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PlazoCreditoDias = table.Column<int>(type: "integer", nullable: false, defaultValue: 30),
                    OtifHistorico = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false, defaultValue: 95.0m),
                    Activo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    FechaRegistro = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proveedores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "vendedores",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NombreEmpresa = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Telefono = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EstaActivo = table.Column<bool>(type: "boolean", nullable: false),
                    FechaRegistro = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vendedores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "reglas_comision",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VendedorId = table.Column<Guid>(type: "uuid", nullable: false),
                    PorcentajeComision = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    EsActiva = table.Column<bool>(type: "boolean", nullable: false),
                    VigenteDesde = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    VigenteHasta = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reglas_comision", x => x.Id);
                    table.ForeignKey(
                        name: "FK_reglas_comision_vendedores_VendedorId",
                        column: x => x.VendedorId,
                        principalSchema: "core",
                        principalTable: "vendedores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_categorias_CategoriaPadreId",
                schema: "core",
                table: "categorias",
                column: "CategoriaPadreId");

            migrationBuilder.CreateIndex(
                name: "IX_compradores_Email",
                schema: "core",
                table: "compradores",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_proveedores_Nit",
                schema: "core",
                table: "proveedores",
                column: "Nit");

            migrationBuilder.CreateIndex(
                name: "IX_proveedores_NombreComercial",
                schema: "core",
                table: "proveedores",
                column: "NombreComercial");

            migrationBuilder.CreateIndex(
                name: "IX_reglas_comision_VendedorId",
                schema: "core",
                table: "reglas_comision",
                column: "VendedorId");

            migrationBuilder.CreateIndex(
                name: "IX_vendedores_Email",
                schema: "core",
                table: "vendedores",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "categorias",
                schema: "core");

            migrationBuilder.DropTable(
                name: "compradores",
                schema: "core");

            migrationBuilder.DropTable(
                name: "proveedores",
                schema: "core");

            migrationBuilder.DropTable(
                name: "reglas_comision",
                schema: "core");

            migrationBuilder.DropTable(
                name: "vendedores",
                schema: "core");
        }
    }
}
