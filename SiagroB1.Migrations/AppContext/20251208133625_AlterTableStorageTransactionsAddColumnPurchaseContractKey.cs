#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTableStorageTransactionsAddColumnPurchaseContractKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_STORAGE_ADDRESSES_StorageAddressKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_ADDRESSES_Code",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.AlterColumn<Guid>(
                name: "StorageAddressKey",
                table: "STORAGE_TRANSACTIONS",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseContractKey",
                table: "STORAGE_TRANSACTIONS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreationDate",
                table: "STORAGE_ADDRESSES",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "STORAGE_ADDRESSES",
                type: "VARCHAR(50)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "VARCHAR(50)");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_TRANSACTIONS_PurchaseContractKey",
                table: "STORAGE_TRANSACTIONS",
                column: "PurchaseContractKey");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_ADDRESSES_Code",
                table: "STORAGE_ADDRESSES",
                column: "Code",
                unique: true,
                filter: "[Code] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_PURCHASE_CONTRACTS_PurchaseContractKey",
                table: "STORAGE_TRANSACTIONS",
                column: "PurchaseContractKey",
                principalTable: "PURCHASE_CONTRACTS",
                principalColumn: "Key");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_STORAGE_ADDRESSES_StorageAddressKey",
                table: "STORAGE_TRANSACTIONS",
                column: "StorageAddressKey",
                principalTable: "STORAGE_ADDRESSES",
                principalColumn: "Key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_PURCHASE_CONTRACTS_PurchaseContractKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_STORAGE_ADDRESSES_StorageAddressKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_TRANSACTIONS_PurchaseContractKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_ADDRESSES_Code",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.DropColumn(
                name: "PurchaseContractKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.AlterColumn<Guid>(
                name: "StorageAddressKey",
                table: "STORAGE_TRANSACTIONS",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreationDate",
                table: "STORAGE_ADDRESSES",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "STORAGE_ADDRESSES",
                type: "VARCHAR(50)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "VARCHAR(50)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_ADDRESSES_Code",
                table: "STORAGE_ADDRESSES",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_STORAGE_ADDRESSES_StorageAddressKey",
                table: "STORAGE_TRANSACTIONS",
                column: "StorageAddressKey",
                principalTable: "STORAGE_ADDRESSES",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
