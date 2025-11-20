using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AlterTablePurchaseContractsAddColumnSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PURCHASE_CONTRACTS_Code",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.AddColumn<string>(
                name: "Sequence",
                table: "PURCHASE_CONTRACTS",
                type: "VARCHAR(3)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_CONTRACTS_Code_Sequence",
                table: "PURCHASE_CONTRACTS",
                columns: new[] { "Code", "Sequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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
                column: "Code");
        }
    }
}
