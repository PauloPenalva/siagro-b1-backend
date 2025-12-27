#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTablePurchaseContractDropColumnSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PURCHASE_CONTRACTS_Code_Sequence",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "Sequence",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_CONTRACTS_Code",
                table: "PURCHASE_CONTRACTS",
                column: "Code",
                unique: true,
                filter: "[Code] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PURCHASE_CONTRACTS_Code",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.AddColumn<string>(
                name: "Sequence",
                table: "PURCHASE_CONTRACTS",
                type: "VARCHAR(3)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_CONTRACTS_Code_Sequence",
                table: "PURCHASE_CONTRACTS",
                columns: new[] { "Code", "Sequence" },
                unique: true,
                filter: "[Code] IS NOT NULL");
        }
    }
}
