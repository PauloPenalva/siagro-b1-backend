#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTableShipmentReleasesAddColumnDeliveryLocationCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeliveryLocationCode",
                table: "SHIPMENT_RELEASES",
                type: "VARCHAR(10)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_RELEASES_DeliveryLocationCode",
                table: "SHIPMENT_RELEASES",
                column: "DeliveryLocationCode");

            migrationBuilder.AddForeignKey(
                name: "FK_SHIPMENT_RELEASES_WAREHOUSES_DeliveryLocationCode",
                table: "SHIPMENT_RELEASES",
                column: "DeliveryLocationCode",
                principalTable: "WAREHOUSES",
                principalColumn: "Code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SHIPMENT_RELEASES_WAREHOUSES_DeliveryLocationCode",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.DropIndex(
                name: "IX_SHIPMENT_RELEASES_DeliveryLocationCode",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.DropColumn(
                name: "DeliveryLocationCode",
                table: "SHIPMENT_RELEASES");
        }
    }
}
