using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddStorageTransactionsAvailableShipmentsIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_TRANSACTIONS_AvailableShipments",
                table: "STORAGE_TRANSACTIONS",
                columns: new[] { "TransactionType", "TransactionStatus" },
                filter: "[ShipmentLoadKey] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_STORAGE_TRANSACTIONS_AvailableShipments",
                table: "STORAGE_TRANSACTIONS");
        }
    }
}
