using MarketplaceERP.Catalog.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketplaceERP.Catalog.Migrations;

/// <summary>
/// Unicidad física de referencias WMS. Fallar cerrado ante duplicados legados:
/// NUNCA renumerar documentos/despachos ya emitidos de manera automática.
/// Solo se aplica a un PostgreSQL LOCAL durante QA.
/// </summary>
[DbContext(typeof(CatalogDbContext))]
[Migration("20261110013000_WmsReferenciasExternasUnicas")]
public partial class WmsReferenciasExternasUnicas : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM catalog."PickLists"
                    GROUP BY "NumeroPickList"
                    HAVING count(*) > 1
                ) THEN
                    RAISE EXCEPTION
                        'WMS: existen PickLists con NumeroPickList duplicado; detener migración y conciliar';
                END IF;

                IF EXISTS (
                    SELECT 1 FROM catalog."PaquetesDespacho"
                    GROUP BY "NumeroGuia"
                    HAVING count(*) > 1
                ) THEN
                    RAISE EXCEPTION
                        'WMS: existen paquetes con NumeroGuia duplicado; detener migración y conciliar';
                END IF;

                IF EXISTS (
                    SELECT 1 FROM catalog."PickLists"
                    GROUP BY "OrdenId"
                    HAVING count(*) > 1
                ) THEN
                    RAISE EXCEPTION
                        'WMS: existen PickLists historicas duplicadas por OrdenId; detener migración';
                END IF;

                IF EXISTS (
                    SELECT 1 FROM catalog."PaquetesDespacho"
                    GROUP BY "OrdenId"
                    HAVING count(*) > 1
                ) THEN
                    RAISE EXCEPTION
                        'WMS: existen paquetes historicos duplicados por OrdenId; detener migración';
                END IF;
            END $$;
            """);

        migrationBuilder.CreateIndex(
            name: "UX_WMS_PickList_NumeroPickList",
            schema: "catalog",
            table: "PickLists",
            column: "NumeroPickList",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "UX_WMS_Paquete_NumeroGuia",
            schema: "catalog",
            table: "PaquetesDespacho",
            column: "NumeroGuia",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "UX_WMS_PickList_OrdenId",
            schema: "catalog",
            table: "PickLists",
            column: "OrdenId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "UX_WMS_Paquete_OrdenId",
            schema: "catalog",
            table: "PaquetesDespacho",
            column: "OrdenId",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "UX_WMS_Paquete_OrdenId",
            schema: "catalog",
            table: "PaquetesDespacho");

        migrationBuilder.DropIndex(
            name: "UX_WMS_PickList_OrdenId",
            schema: "catalog",
            table: "PickLists");

        migrationBuilder.DropIndex(
            name: "UX_WMS_Paquete_NumeroGuia",
            schema: "catalog",
            table: "PaquetesDespacho");

        migrationBuilder.DropIndex(
            name: "UX_WMS_PickList_NumeroPickList",
            schema: "catalog",
            table: "PickLists");
    }
}
