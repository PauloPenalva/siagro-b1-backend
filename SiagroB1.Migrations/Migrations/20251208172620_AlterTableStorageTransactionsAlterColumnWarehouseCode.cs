using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AlterTableStorageTransactionsAlterColumnWarehouseCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_WAREHOUSES_DeliveryLocationCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.RenameColumn(
                name: "DeliveryLocationCode",
                table: "STORAGE_TRANSACTIONS",
                newName: "WarehouseCode");

            migrationBuilder.RenameIndex(
                name: "IX_STORAGE_TRANSACTIONS_DeliveryLocationCode",
                table: "STORAGE_TRANSACTIONS",
                newName: "IX_STORAGE_TRANSACTIONS_WarehouseCode");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_WAREHOUSES_WarehouseCode",
                table: "STORAGE_TRANSACTIONS",
                column: "WarehouseCode",
                principalTable: "WAREHOUSES",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_WAREHOUSES_WarehouseCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.RenameColumn(
                name: "WarehouseCode",
                table: "STORAGE_TRANSACTIONS",
                newName: "DeliveryLocationCode");

            migrationBuilder.RenameIndex(
                name: "IX_STORAGE_TRANSACTIONS_WarehouseCode",
                table: "STORAGE_TRANSACTIONS",
                newName: "IX_STORAGE_TRANSACTIONS_DeliveryLocationCode");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_WAREHOUSES_DeliveryLocationCode",
                table: "STORAGE_TRANSACTIONS",
                column: "DeliveryLocationCode",
                principalTable: "WAREHOUSES",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
