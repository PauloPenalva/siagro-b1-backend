#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTablePurchaseContractsAlterIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PURCHASE_CONTRACTS_Code_Sequence",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_CONTRACTS_Code_Sequence",
                table: "PURCHASE_CONTRACTS",
                columns: new[] { "Code", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PURCHASE_CONTRACTS_Code_Sequence",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_CONTRACTS_Code_Sequence",
                table: "PURCHASE_CONTRACTS",
                columns: new[] { "Code", "Sequence" });
        }
    }
}
