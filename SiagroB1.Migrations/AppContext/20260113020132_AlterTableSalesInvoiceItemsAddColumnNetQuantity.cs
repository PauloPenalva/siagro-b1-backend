using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTableSalesInvoiceItemsAddColumnNetQuantity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DeliveredQuantity",
                table: "SALES_INVOICES_ITEMS",
                type: "DECIMAL(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "NetQuantity",
                table: "SALES_INVOICES_ITEMS",
                type: "DECIMAL(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "QuantityLoss",
                table: "SALES_INVOICES_ITEMS",
                type: "DECIMAL(18,3)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeliveredQuantity",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "NetQuantity",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "QuantityLoss",
                table: "SALES_INVOICES_ITEMS");
        }
    }
}
