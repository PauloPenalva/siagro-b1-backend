using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddDenormalizedNamesForSapEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UnitOfMeasureName",
                table: "STORAGE_TRANSACTIONS",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UoMName",
                table: "STORAGE_ADDRESSES",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UnitOfMeasureName",
                table: "SALES_CONTRACTS",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CardName",
                table: "PURCHASE_CONTRACTS_BROKERS",
                type: "VARCHAR(200)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UnitOfMeasureName",
                table: "PURCHASE_CONTRACTS",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UomName",
                table: "OWNERSHIP_TRANSFER",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_TRANSACTIONS_TruckDriverCode",
                table: "STORAGE_TRANSACTIONS",
                column: "TruckDriverCode");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_INVOICES_STORAGE_ADDRESSES_StorageAddressCode",
                table: "STORAGE_INVOICES",
                column: "StorageAddressCode",
                principalTable: "STORAGE_ADDRESSES",
                principalColumn: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_TRUCK_DRIVERS_TruckDriverCode",
                table: "STORAGE_TRANSACTIONS",
                column: "TruckDriverCode",
                principalTable: "TRUCK_DRIVERS",
                principalColumn: "Code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_INVOICES_STORAGE_ADDRESSES_StorageAddressCode",
                table: "STORAGE_INVOICES");

            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_TRUCK_DRIVERS_TruckDriverCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_TRANSACTIONS_TruckDriverCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "UnitOfMeasureName",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "UoMName",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.DropColumn(
                name: "UnitOfMeasureName",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "CardName",
                table: "PURCHASE_CONTRACTS_BROKERS");

            migrationBuilder.DropColumn(
                name: "UnitOfMeasureName",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "UomName",
                table: "OWNERSHIP_TRANSFER");
        }
    }
}
