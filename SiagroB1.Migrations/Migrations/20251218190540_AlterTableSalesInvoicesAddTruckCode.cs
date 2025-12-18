using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AlterTableSalesInvoicesAddTruckCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ItemName",
                table: "SALES_INVOICES_ITEMS",
                type: "VARCHAR(200)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UnitOfMeasureCode",
                table: "SALES_INVOICES_ITEMS",
                type: "VARCHAR(4)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "Comments",
                table: "SALES_INVOICES",
                type: "VARCHAR(500)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CardName",
                table: "SALES_INVOICES",
                type: "VARCHAR(200)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GrossWeight",
                table: "SALES_INVOICES",
                type: "decimal(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "NetWeight",
                table: "SALES_INVOICES",
                type: "decimal(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "TruckCode",
                table: "SALES_INVOICES",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TruckingCompanyCode",
                table: "SALES_INVOICES",
                type: "VARCHAR(15)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TruckingCompanyName",
                table: "SALES_INVOICES",
                type: "VARCHAR(200)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SALES_INVOICES_ITEMS_UnitOfMeasureCode",
                table: "SALES_INVOICES_ITEMS",
                column: "UnitOfMeasureCode");

            migrationBuilder.AddForeignKey(
                name: "FK_SALES_INVOICES_ITEMS_UNITS_OF_MEASURE_UnitOfMeasureCode",
                table: "SALES_INVOICES_ITEMS",
                column: "UnitOfMeasureCode",
                principalTable: "UNITS_OF_MEASURE",
                principalColumn: "Code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SALES_INVOICES_ITEMS_UNITS_OF_MEASURE_UnitOfMeasureCode",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropIndex(
                name: "IX_SALES_INVOICES_ITEMS_UnitOfMeasureCode",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "ItemName",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "UnitOfMeasureCode",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "CardName",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "GrossWeight",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "NetWeight",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "TruckCode",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "TruckingCompanyCode",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "TruckingCompanyName",
                table: "SALES_INVOICES");

            migrationBuilder.AlterColumn<string>(
                name: "Comments",
                table: "SALES_INVOICES",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "VARCHAR(500)",
                oldNullable: true);
        }
    }
}
