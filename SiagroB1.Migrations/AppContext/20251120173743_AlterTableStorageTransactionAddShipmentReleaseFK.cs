#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTableStorageTransactionAddShipmentReleaseFK : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_TRANSACTIONS_ShipmentReleaseKey",
                table: "STORAGE_TRANSACTIONS",
                column: "ShipmentReleaseKey");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_SHIPMENT_RELEASES_ShipmentReleaseKey",
                table: "STORAGE_TRANSACTIONS",
                column: "ShipmentReleaseKey",
                principalTable: "SHIPMENT_RELEASES",
                principalColumn: "Key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_SHIPMENT_RELEASES_ShipmentReleaseKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_TRANSACTIONS_ShipmentReleaseKey",
                table: "STORAGE_TRANSACTIONS");
        }
    }
}
