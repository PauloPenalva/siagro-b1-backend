using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class ZeroDeliveryDifferenceForUnconferredItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "DeliveryDifference",
                table: "SALES_INVOICES_ITEMS",
                type: "DECIMAL(18,3)",
                nullable: false,
                computedColumnSql: "CASE WHEN [DeliveredQuantity] = 0 AND [DeliveryStatus] = 0 THEN 0 ELSE [DeliveredQuantity] - [Quantity] END",
                stored: true,
                oldClrType: typeof(decimal),
                oldType: "DECIMAL(18,3)",
                oldComputedColumnSql: "[DeliveredQuantity] - [Quantity]",
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
                computedColumnSql: "[DeliveredQuantity] - [Quantity]",
                stored: true,
                oldClrType: typeof(decimal),
                oldType: "DECIMAL(18,3)",
                oldComputedColumnSql: "CASE WHEN [DeliveredQuantity] = 0 AND [DeliveryStatus] = 0 THEN 0 ELSE [DeliveredQuantity] - [Quantity] END",
                oldStored: true);
        }
    }
}
