using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddFunruralRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "FunruralInssRate",
                table: "USAGES",
                type: "DECIMAL(7,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FunruralRatRate",
                table: "USAGES",
                type: "DECIMAL(7,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FunruralSenarRate",
                table: "USAGES",
                type: "DECIMAL(7,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FunruralBase",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FunruralInssRate",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(7,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FunruralInssValue",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FunruralRatRate",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(7,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FunruralRatValue",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FunruralSenarRate",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(7,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FunruralSenarValue",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FunruralInssRate",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "FunruralRatRate",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "FunruralSenarRate",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "FunruralBase",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "FunruralInssRate",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "FunruralInssValue",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "FunruralRatRate",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "FunruralRatValue",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "FunruralSenarRate",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "FunruralSenarValue",
                table: "PURCHASE_INVOICES_ITEMS");
        }
    }
}
