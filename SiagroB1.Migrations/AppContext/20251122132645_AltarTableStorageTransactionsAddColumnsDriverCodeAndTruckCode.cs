#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AltarTableStorageTransactionsAddColumnsDriverCodeAndTruckCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TransactionsStatus",
                table: "STORAGE_TRANSACTIONS",
                newName: "TransactionStatus");

            migrationBuilder.RenameColumn(
                name: "NetWeight",
                table: "STORAGE_TRANSACTIONS",
                newName: "Volume");

            migrationBuilder.RenameColumn(
                name: "TruckDriver",
                table: "SHIPPING_ORDERS",
                newName: "TruckDriverCode");

            migrationBuilder.AddColumn<string>(
                name: "TruckCode",
                table: "STORAGE_TRANSACTIONS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TruckDriverCode",
                table: "STORAGE_TRANSACTIONS",
                type: "VARCHAR(11)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TruckCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "TruckDriverCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.RenameColumn(
                name: "Volume",
                table: "STORAGE_TRANSACTIONS",
                newName: "NetWeight");

            migrationBuilder.RenameColumn(
                name: "TransactionStatus",
                table: "STORAGE_TRANSACTIONS",
                newName: "TransactionsStatus");

            migrationBuilder.RenameColumn(
                name: "TruckDriverCode",
                table: "SHIPPING_ORDERS",
                newName: "TruckDriver");
        }
    }
}
