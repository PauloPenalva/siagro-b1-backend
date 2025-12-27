#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTableStoraTransactionsAddColumnCardCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_PURCHASE_CONTRACTS_PurchaseContractKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_TRANSACTIONS_PurchaseContractKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.AddColumn<string>(
                name: "CardCode",
                table: "STORAGE_TRANSACTIONS",
                type: "VARCHAR(10)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CardName",
                table: "STORAGE_TRANSACTIONS",
                type: "VARCHAR(200)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ItemCode",
                table: "STORAGE_TRANSACTIONS",
                type: "VARCHAR(10)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ItemName",
                table: "STORAGE_TRANSACTIONS",
                type: "VARCHAR(200)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CardCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "CardName",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "ItemCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "ItemName",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_TRANSACTIONS_PurchaseContractKey",
                table: "STORAGE_TRANSACTIONS",
                column: "PurchaseContractKey");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_PURCHASE_CONTRACTS_PurchaseContractKey",
                table: "STORAGE_TRANSACTIONS",
                column: "PurchaseContractKey",
                principalTable: "PURCHASE_CONTRACTS",
                principalColumn: "Key");
        }
    }
}
