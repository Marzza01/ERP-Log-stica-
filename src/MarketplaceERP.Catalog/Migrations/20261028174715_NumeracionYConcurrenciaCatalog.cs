using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketplaceERP.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class NumeracionYConcurrenciaCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "NumeroGuiaSeq",
                schema: "catalog");

            migrationBuilder.CreateSequence(
                name: "NumeroPickListSeq",
                schema: "catalog");

            migrationBuilder.AlterColumn<string>(
                name: "NumeroPickList",
                schema: "catalog",
                table: "PickLists",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "catalog",
                table: "PickLists",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AlterColumn<string>(
                name: "NumeroGuia",
                schema: "catalog",
                table: "PaquetesDespacho",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.CreateIndex(
                name: "IX_PickLists_NumeroPickList",
                schema: "catalog",
                table: "PickLists",
                column: "NumeroPickList",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaquetesDespacho_NumeroGuia",
                schema: "catalog",
                table: "PaquetesDespacho",
                column: "NumeroGuia",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PickLists_NumeroPickList",
                schema: "catalog",
                table: "PickLists");

            migrationBuilder.DropIndex(
                name: "IX_PaquetesDespacho_NumeroGuia",
                schema: "catalog",
                table: "PaquetesDespacho");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "catalog",
                table: "PickLists");

            migrationBuilder.DropSequence(
                name: "NumeroGuiaSeq",
                schema: "catalog");

            migrationBuilder.DropSequence(
                name: "NumeroPickListSeq",
                schema: "catalog");

            migrationBuilder.AlterColumn<string>(
                name: "NumeroPickList",
                schema: "catalog",
                table: "PickLists",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "NumeroGuia",
                schema: "catalog",
                table: "PaquetesDespacho",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);
        }
    }
}
