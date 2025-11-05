using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class FixWarehousesTableName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_LOTS_WHAREHOUSES_WhareHouseKey",
                table: "STORAGE_LOTS");

            migrationBuilder.DropForeignKey(
                name: "FK_WHAREHOUSES_BRANCHS_BranchKey",
                table: "WHAREHOUSES");

            migrationBuilder.DropPrimaryKey(
                name: "PK_WHAREHOUSES",
                table: "WHAREHOUSES");

            migrationBuilder.RenameTable(
                name: "WHAREHOUSES",
                newName: "WAREHOUSES");

            migrationBuilder.RenameIndex(
                name: "IX_WHAREHOUSES_BranchKey",
                table: "WAREHOUSES",
                newName: "IX_WAREHOUSES_BranchKey");

            migrationBuilder.AddPrimaryKey(
                name: "PK_WAREHOUSES",
                table: "WAREHOUSES",
                column: "Key");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_LOTS_WAREHOUSES_WhareHouseKey",
                table: "STORAGE_LOTS",
                column: "WhareHouseKey",
                principalTable: "WAREHOUSES",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WAREHOUSES_BRANCHS_BranchKey",
                table: "WAREHOUSES",
                column: "BranchKey",
                principalTable: "BRANCHS",
                principalColumn: "Key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_LOTS_WAREHOUSES_WhareHouseKey",
                table: "STORAGE_LOTS");

            migrationBuilder.DropForeignKey(
                name: "FK_WAREHOUSES_BRANCHS_BranchKey",
                table: "WAREHOUSES");

            migrationBuilder.DropPrimaryKey(
                name: "PK_WAREHOUSES",
                table: "WAREHOUSES");

            migrationBuilder.RenameTable(
                name: "WAREHOUSES",
                newName: "WHAREHOUSES");

            migrationBuilder.RenameIndex(
                name: "IX_WAREHOUSES_BranchKey",
                table: "WHAREHOUSES",
                newName: "IX_WHAREHOUSES_BranchKey");

            migrationBuilder.AddPrimaryKey(
                name: "PK_WHAREHOUSES",
                table: "WHAREHOUSES",
                column: "Key");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_LOTS_WHAREHOUSES_WhareHouseKey",
                table: "STORAGE_LOTS",
                column: "WhareHouseKey",
                principalTable: "WHAREHOUSES",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WHAREHOUSES_BRANCHS_BranchKey",
                table: "WHAREHOUSES",
                column: "BranchKey",
                principalTable: "BRANCHS",
                principalColumn: "Key");
        }
    }
}
