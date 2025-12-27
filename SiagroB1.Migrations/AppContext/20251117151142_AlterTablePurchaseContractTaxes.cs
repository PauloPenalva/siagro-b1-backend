#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTablePurchaseContractTaxes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_CONTRACTS_TAXES_TaxCode",
                table: "PURCHASE_CONTRACTS_TAXES",
                column: "TaxCode");

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_CONTRACTS_TAXES_TAXES_TaxCode",
                table: "PURCHASE_CONTRACTS_TAXES",
                column: "TaxCode",
                principalTable: "TAXES",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_CONTRACTS_TAXES_TAXES_TaxCode",
                table: "PURCHASE_CONTRACTS_TAXES");

            migrationBuilder.DropIndex(
                name: "IX_PURCHASE_CONTRACTS_TAXES_TaxCode",
                table: "PURCHASE_CONTRACTS_TAXES");
        }
    }
}
