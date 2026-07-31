using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class InvertSalesInvoiceItemDeliveryDifference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "DeliveryDifference",
                table: "SALES_INVOICES_ITEMS",
                type: "DECIMAL(18,3)",
                nullable: false,
                computedColumnSql: "[DeliveredQuantity] - [Quantity]",
                stored: true,
                oldClrType: typeof(decimal),
                oldType: "DECIMAL(18,3)",
                oldComputedColumnSql: "[Quantity] - [DeliveredQuantity]",
                oldStored: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "DeliveryDifference",
                table: "SALES_INVOICES_ITEMS",
                type: "DECIMAL(18,3)",
                nullable: false,
                computedColumnSql: "[Quantity] - [DeliveredQuantity]",
                stored: true,
                oldClrType: typeof(decimal),
                oldType: "DECIMAL(18,3)",
                oldComputedColumnSql: "[DeliveredQuantity] - [Quantity]",
                oldStored: true);
        }
    }
}
