using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddShipmentLoadTransitLoss : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TransitLossReason",
                table: "STORAGE_TRANSACTIONS",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TransitLossQuantity",
                table: "SHIPMENT_LOADS",
                type: "DECIMAL(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "TransitLossReason",
                table: "SHIPMENT_LOAD_REFUSALS",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TransitLossReason",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "TransitLossQuantity",
                table: "SHIPMENT_LOADS");

            migrationBuilder.DropColumn(
                name: "TransitLossReason",
                table: "SHIPMENT_LOAD_REFUSALS");
        }
    }
}
