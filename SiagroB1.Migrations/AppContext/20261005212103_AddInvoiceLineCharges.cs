using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddInvoiceLineCharges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DiscountValue",
                table: "SALES_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FreightValue",
                table: "SALES_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "InsuranceValue",
                table: "SALES_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OtherExpensesValue",
                table: "SALES_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountValue",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FreightValue",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "InsuranceValue",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OtherExpensesValue",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiscountValue",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "FreightValue",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "InsuranceValue",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "OtherExpensesValue",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "DiscountValue",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "FreightValue",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "InsuranceValue",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "OtherExpensesValue",
                table: "PURCHASE_INVOICES_ITEMS");
        }
    }
}
