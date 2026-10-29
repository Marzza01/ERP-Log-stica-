using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketplaceERP.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class Fase4AInventarioMultiubicacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_stock_fisico_ProductoId",
                schema: "catalog",
                table: "stock_fisico");

            migrationBuilder.RenameColumn(
                name: "Cantidad",
                schema: "catalog",
                table: "reservas_stock",
                newName: "CantidadOriginal");

            migrationBuilder.AddColumn<int>(
                name: "CantidadBloqueada",
                schema: "catalog",
                table: "stock_fisico",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "Estado",
                schema: "catalog",
                table: "reservas_stock",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<int>(
                name: "CantidadConsumida",
                schema: "catalog",
                table: "reservas_stock",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CantidadLiberada",
                schema: "catalog",
                table: "reservas_stock",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaFinalizacion",
                schema: "catalog",
                table: "reservas_stock",
                type: "timestamp with time zone",
                nullable: true);

            // Normaliza las reservas creadas con el modelo anterior.
            // Sin este paso, una reserva antigua "Liberada" o "Despachada" conservaría
            // el estado pero tendría CantidadLiberada/CantidadConsumida en 0.
            migrationBuilder.Sql(
                """
                UPDATE catalog.reservas_stock
                SET "CantidadLiberada" = "CantidadOriginal",
                    "FechaFinalizacion" = COALESCE("FechaConfirmacion", "FechaReserva")
                WHERE "Estado" = 'Liberada';

                UPDATE catalog.reservas_stock
                SET "CantidadConsumida" = "CantidadOriginal",
                    "Estado" = 'Consumida',
                    "FechaFinalizacion" = COALESCE("FechaConfirmacion", "FechaReserva")
                WHERE "Estado" = 'Despachada';
                """);

            migrationBuilder.CreateTable(
                name: "movimientos_inventario",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductoId = table.Column<Guid>(type: "uuid", nullable: false),
                    TipoMovimiento = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Cantidad = table.Column<int>(type: "integer", nullable: false),
                    UbicacionOrigenId = table.Column<Guid>(type: "uuid", nullable: true),
                    UbicacionDestinoId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReservaStockId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrdenId = table.Column<Guid>(type: "uuid", nullable: true),
                    RmaId = table.Column<Guid>(type: "uuid", nullable: true),
                    Referencia = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Observaciones = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FechaMovimiento = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_movimientos_inventario", x => x.Id);
                    table.CheckConstraint("CK_movimientos_inventario_cantidad_positiva", "\"Cantidad\" > 0");
                    table.ForeignKey(
                        name: "FK_movimientos_inventario_productos_ProductoId",
                        column: x => x.ProductoId,
                        principalSchema: "catalog",
                        principalTable: "productos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_movimientos_inventario_reservas_stock_ReservaStockId",
                        column: x => x.ReservaStockId,
                        principalSchema: "catalog",
                        principalTable: "reservas_stock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_movimientos_inventario_ubicaciones_almacen_UbicacionDestino~",
                        column: x => x.UbicacionDestinoId,
                        principalSchema: "catalog",
                        principalTable: "ubicaciones_almacen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_movimientos_inventario_ubicaciones_almacen_UbicacionOrigenId",
                        column: x => x.UbicacionOrigenId,
                        principalSchema: "catalog",
                        principalTable: "ubicaciones_almacen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reservas_stock_detalle",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReservaStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    StockFisicoId = table.Column<Guid>(type: "uuid", nullable: false),
                    CantidadAsignada = table.Column<int>(type: "integer", nullable: false),
                    CantidadLiberada = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CantidadConsumida = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reservas_stock_detalle", x => x.Id);
                    table.CheckConstraint("CK_reservas_stock_detalle_cantidad_resuelta", "\"CantidadLiberada\" + \"CantidadConsumida\" <= \"CantidadAsignada\"");
                    table.CheckConstraint("CK_reservas_stock_detalle_cantidades_no_negativas", "\"CantidadAsignada\" >= 0 AND \"CantidadLiberada\" >= 0 AND \"CantidadConsumida\" >= 0");
                    table.ForeignKey(
                        name: "FK_reservas_stock_detalle_reservas_stock_ReservaStockId",
                        column: x => x.ReservaStockId,
                        principalSchema: "catalog",
                        principalTable: "reservas_stock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_reservas_stock_detalle_stock_fisico_StockFisicoId",
                        column: x => x.StockFisicoId,
                        principalSchema: "catalog",
                        principalTable: "stock_fisico",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // El modelo anterior garantizaba un único StockFisico por Producto.
            // Toda reserva histórica debe poder asignarse a ese saldo antes de pasar
            // al modelo multiubicación. Si falta el saldo, se detiene la migración
            // en lugar de dejar una reserva histórica parcialmente migrada.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM catalog.reservas_stock r
                        LEFT JOIN catalog.stock_fisico s ON s."ProductoId" = r."ProductoId"
                        WHERE s."Id" IS NULL
                    ) THEN
                        RAISE EXCEPTION 'Fase4A: existen reservas_stock sin stock_fisico asociado; corrija esos datos antes de migrar.';
                    END IF;
                END $$;

                INSERT INTO catalog.reservas_stock_detalle
                    ("Id", "ReservaStockId", "StockFisicoId", "CantidadAsignada", "CantidadLiberada", "CantidadConsumida")
                SELECT
                    r."Id",
                    r."Id",
                    s."Id",
                    r."CantidadOriginal",
                    r."CantidadLiberada",
                    r."CantidadConsumida"
                FROM catalog.reservas_stock r
                INNER JOIN catalog.stock_fisico s ON s."ProductoId" = r."ProductoId";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_stock_fisico_ProductoId",
                schema: "catalog",
                table: "stock_fisico",
                column: "ProductoId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_fisico_ProductoId_UbicacionAlmacenId",
                schema: "catalog",
                table: "stock_fisico",
                columns: new[] { "ProductoId", "UbicacionAlmacenId" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_stock_fisico_cantidades_no_negativas",
                schema: "catalog",
                table: "stock_fisico",
                sql: "\"CantidadTotal\" >= 0 AND \"CantidadReservada\" >= 0 AND \"CantidadBloqueada\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_stock_fisico_reservado_bloqueado",
                schema: "catalog",
                table: "stock_fisico",
                sql: "\"CantidadReservada\" + \"CantidadBloqueada\" <= \"CantidadTotal\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_reservas_stock_cantidad_resuelta",
                schema: "catalog",
                table: "reservas_stock",
                sql: "\"CantidadLiberada\" + \"CantidadConsumida\" <= \"CantidadOriginal\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_reservas_stock_cantidades_no_negativas",
                schema: "catalog",
                table: "reservas_stock",
                sql: "\"CantidadOriginal\" >= 0 AND \"CantidadLiberada\" >= 0 AND \"CantidadConsumida\" >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_movimientos_inventario_FechaMovimiento",
                schema: "catalog",
                table: "movimientos_inventario",
                column: "FechaMovimiento");

            migrationBuilder.CreateIndex(
                name: "IX_movimientos_inventario_OrdenId",
                schema: "catalog",
                table: "movimientos_inventario",
                column: "OrdenId");

            migrationBuilder.CreateIndex(
                name: "IX_movimientos_inventario_ProductoId",
                schema: "catalog",
                table: "movimientos_inventario",
                column: "ProductoId");

            migrationBuilder.CreateIndex(
                name: "IX_movimientos_inventario_ReservaStockId",
                schema: "catalog",
                table: "movimientos_inventario",
                column: "ReservaStockId");

            migrationBuilder.CreateIndex(
                name: "IX_movimientos_inventario_RmaId",
                schema: "catalog",
                table: "movimientos_inventario",
                column: "RmaId");

            migrationBuilder.CreateIndex(
                name: "IX_movimientos_inventario_UbicacionDestinoId",
                schema: "catalog",
                table: "movimientos_inventario",
                column: "UbicacionDestinoId");

            migrationBuilder.CreateIndex(
                name: "IX_movimientos_inventario_UbicacionOrigenId",
                schema: "catalog",
                table: "movimientos_inventario",
                column: "UbicacionOrigenId");

            migrationBuilder.CreateIndex(
                name: "IX_reservas_stock_detalle_ReservaStockId",
                schema: "catalog",
                table: "reservas_stock_detalle",
                column: "ReservaStockId");

            migrationBuilder.CreateIndex(
                name: "IX_reservas_stock_detalle_ReservaStockId_StockFisicoId",
                schema: "catalog",
                table: "reservas_stock_detalle",
                columns: new[] { "ReservaStockId", "StockFisicoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reservas_stock_detalle_StockFisicoId",
                schema: "catalog",
                table: "reservas_stock_detalle",
                column: "StockFisicoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // El esquema anterior no conoce los estados parciales de Fase 4A.
            // Se mapean a su equivalente legado antes de reducir Estado a varchar(20).
            migrationBuilder.Sql(
                """
                UPDATE catalog.reservas_stock
                SET "Estado" = CASE
                    WHEN "Estado" IN ('Consumida', 'Finalizada') THEN 'Despachada'
                    WHEN "Estado" IN ('ParcialmenteLiberada', 'ParcialmenteConsumida') THEN 'Confirmada'
                    WHEN "Estado" = 'Expirada' THEN 'Liberada'
                    ELSE "Estado"
                END;
                """);

            migrationBuilder.DropTable(
                name: "movimientos_inventario",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "reservas_stock_detalle",
                schema: "catalog");

            migrationBuilder.DropIndex(
                name: "IX_stock_fisico_ProductoId",
                schema: "catalog",
                table: "stock_fisico");

            migrationBuilder.DropIndex(
                name: "IX_stock_fisico_ProductoId_UbicacionAlmacenId",
                schema: "catalog",
                table: "stock_fisico");

            migrationBuilder.DropCheckConstraint(
                name: "CK_stock_fisico_cantidades_no_negativas",
                schema: "catalog",
                table: "stock_fisico");

            migrationBuilder.DropCheckConstraint(
                name: "CK_stock_fisico_reservado_bloqueado",
                schema: "catalog",
                table: "stock_fisico");

            migrationBuilder.DropCheckConstraint(
                name: "CK_reservas_stock_cantidad_resuelta",
                schema: "catalog",
                table: "reservas_stock");

            migrationBuilder.DropCheckConstraint(
                name: "CK_reservas_stock_cantidades_no_negativas",
                schema: "catalog",
                table: "reservas_stock");

            migrationBuilder.DropColumn(
                name: "CantidadBloqueada",
                schema: "catalog",
                table: "stock_fisico");

            migrationBuilder.DropColumn(
                name: "CantidadConsumida",
                schema: "catalog",
                table: "reservas_stock");

            migrationBuilder.DropColumn(
                name: "CantidadLiberada",
                schema: "catalog",
                table: "reservas_stock");

            migrationBuilder.DropColumn(
                name: "FechaFinalizacion",
                schema: "catalog",
                table: "reservas_stock");

            migrationBuilder.RenameColumn(
                name: "CantidadOriginal",
                schema: "catalog",
                table: "reservas_stock",
                newName: "Cantidad");

            migrationBuilder.AlterColumn<string>(
                name: "Estado",
                schema: "catalog",
                table: "reservas_stock",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            // Un rollback al modelo 1:1 solo es posible si todavía existe un único
            // saldo físico por producto. Evita una pérdida silenciosa de datos.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT "ProductoId"
                        FROM catalog.stock_fisico
                        GROUP BY "ProductoId"
                        HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'No se puede revertir Fase4A: existen productos con stock en múltiples ubicaciones.';
                    END IF;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_stock_fisico_ProductoId",
                schema: "catalog",
                table: "stock_fisico",
                column: "ProductoId",
                unique: true);
        }
    }
}
