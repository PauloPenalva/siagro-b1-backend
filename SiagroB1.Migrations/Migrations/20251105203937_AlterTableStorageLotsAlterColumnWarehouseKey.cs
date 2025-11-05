using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AlterTableStorageLotsAlterColumnWarehouseKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_LOTS_WAREHOUSES_WhareHouseKey",
                table: "STORAGE_LOTS");

            migrationBuilder.RenameColumn(
                name: "WhareHouseKey",
                table: "STORAGE_LOTS",
                newName: "WarehouseKey");

            migrationBuilder.RenameIndex(
                name: "IX_STORAGE_LOTS_WhareHouseKey",
                table: "STORAGE_LOTS",
                newName: "IX_STORAGE_LOTS_WarehouseKey");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_LOTS_WAREHOUSES_WarehouseKey",
                table: "STORAGE_LOTS",
                column: "WarehouseKey",
                principalTable: "WAREHOUSES",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_LOTS_WAREHOUSES_WarehouseKey",
                table: "STORAGE_LOTS");

            migrationBuilder.RenameColumn(
                name: "WarehouseKey",
                table: "STORAGE_LOTS",
                newName: "WhareHouseKey");

            migrationBuilder.RenameIndex(
                name: "IX_STORAGE_LOTS_WarehouseKey",
                table: "STORAGE_LOTS",
                newName: "IX_STORAGE_LOTS_WhareHouseKey");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_LOTS_WAREHOUSES_WhareHouseKey",
                table: "STORAGE_LOTS",
                column: "WhareHouseKey",
                principalTable: "WAREHOUSES",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
