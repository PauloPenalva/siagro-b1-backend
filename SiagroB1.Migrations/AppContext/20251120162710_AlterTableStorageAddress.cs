#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTableStorageAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_STORAGE_ADDRESSES_StorageAddressId",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.RenameColumn(
                name: "StorageAddressId",
                table: "STORAGE_TRANSACTIONS",
                newName: "StorageAddressKey");

            migrationBuilder.RenameIndex(
                name: "IX_STORAGE_TRANSACTIONS_StorageAddressId",
                table: "STORAGE_TRANSACTIONS",
                newName: "IX_STORAGE_TRANSACTIONS_StorageAddressKey");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "STORAGE_ADDRESSES",
                type: "VARCHAR(50)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "VARCHAR(10)");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_STORAGE_ADDRESSES_StorageAddressKey",
                table: "STORAGE_TRANSACTIONS",
                column: "StorageAddressKey",
                principalTable: "STORAGE_ADDRESSES",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_STORAGE_ADDRESSES_StorageAddressKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.RenameColumn(
                name: "StorageAddressKey",
                table: "STORAGE_TRANSACTIONS",
                newName: "StorageAddressId");

            migrationBuilder.RenameIndex(
                name: "IX_STORAGE_TRANSACTIONS_StorageAddressKey",
                table: "STORAGE_TRANSACTIONS",
                newName: "IX_STORAGE_TRANSACTIONS_StorageAddressId");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "STORAGE_ADDRESSES",
                type: "VARCHAR(10)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "VARCHAR(50)");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_STORAGE_ADDRESSES_StorageAddressId",
                table: "STORAGE_TRANSACTIONS",
                column: "StorageAddressId",
                principalTable: "STORAGE_ADDRESSES",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
