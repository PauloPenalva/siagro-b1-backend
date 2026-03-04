using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTableProcessingCostAddColumnCleaningPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CleaningServicePrice",
                table: "STORAGE_TRANSACTIONS",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ReceiptServicePrice",
                table: "STORAGE_TRANSACTIONS",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ShipmentPrice",
                table: "STORAGE_TRANSACTIONS",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CleaningPrice",
                table: "PROCESSING_COSTS",
                type: "DECIMAL(18,8)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReceiptPrice",
                table: "PROCESSING_COSTS",
                type: "DECIMAL(18,8)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ShipmentPrice",
                table: "PROCESSING_COSTS",
                type: "DECIMAL(18,8)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CleaningServicePrice",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "ReceiptServicePrice",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "ShipmentPrice",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "CleaningPrice",
                table: "PROCESSING_COSTS");

            migrationBuilder.DropColumn(
                name: "ReceiptPrice",
                table: "PROCESSING_COSTS");

            migrationBuilder.DropColumn(
                name: "ShipmentPrice",
                table: "PROCESSING_COSTS");
        }
    }
}
