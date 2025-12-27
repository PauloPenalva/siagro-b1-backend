#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTablePurchaseContractAddColumDocNumberKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_CONTRACTS_DOC_TYPES_DocTypeCode",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropIndex(
                name: "IX_PURCHASE_CONTRACTS_DocTypeCode",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "DocTypeCode",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.AddColumn<Guid>(
                name: "DocNumberKey",
                table: "PURCHASE_CONTRACTS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_CONTRACTS_DocNumberKey",
                table: "PURCHASE_CONTRACTS",
                column: "DocNumberKey");

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_CONTRACTS_DOC_NUMBERS_DocNumberKey",
                table: "PURCHASE_CONTRACTS",
                column: "DocNumberKey",
                principalTable: "DOC_NUMBERS",
                principalColumn: "Key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_CONTRACTS_DOC_NUMBERS_DocNumberKey",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropIndex(
                name: "IX_PURCHASE_CONTRACTS_DocNumberKey",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "DocNumberKey",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.AddColumn<string>(
                name: "DocTypeCode",
                table: "PURCHASE_CONTRACTS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_CONTRACTS_DocTypeCode",
                table: "PURCHASE_CONTRACTS",
                column: "DocTypeCode");

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_CONTRACTS_DOC_TYPES_DocTypeCode",
                table: "PURCHASE_CONTRACTS",
                column: "DocTypeCode",
                principalTable: "DOC_TYPES",
                principalColumn: "Code");
        }
    }
}
