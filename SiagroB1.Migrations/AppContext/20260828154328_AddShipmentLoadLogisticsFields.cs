using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddShipmentLoadLogisticsFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CardCode",
                table: "SHIPMENT_LOADS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CardName",
                table: "SHIPMENT_LOADS",
                type: "VARCHAR(200)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CarrierCardCode",
                table: "SHIPMENT_LOADS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CarrierName",
                table: "SHIPMENT_LOADS",
                type: "VARCHAR(200)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FreightPrice",
                table: "SHIPMENT_LOADS",
                type: "DECIMAL(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasExcess",
                table: "SHIPMENT_LOADS",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CardCode",
                table: "SHIPMENT_LOADS");

            migrationBuilder.DropColumn(
                name: "CardName",
                table: "SHIPMENT_LOADS");

            migrationBuilder.DropColumn(
                name: "CarrierCardCode",
                table: "SHIPMENT_LOADS");

            migrationBuilder.DropColumn(
                name: "CarrierName",
                table: "SHIPMENT_LOADS");

            migrationBuilder.DropColumn(
                name: "FreightPrice",
                table: "SHIPMENT_LOADS");

            migrationBuilder.DropColumn(
                name: "HasExcess",
                table: "SHIPMENT_LOADS");
        }
    }
}
