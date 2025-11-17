using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AlterTableTaxes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_CONTRACTS_TAXES_TAXES_TaxCode",
                table: "PURCHASE_CONTRACTS_TAXES");

            migrationBuilder.DropIndex(
                name: "IX_PURCHASE_CONTRACTS_TAXES_TaxCode",
                table: "PURCHASE_CONTRACTS_TAXES");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "TAXES",
                type: "VARCHAR(15)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "VARCHAR(5)");

            migrationBuilder.AlterColumn<string>(
                name: "TaxCode",
                table: "PURCHASE_CONTRACTS_TAXES",
                type: "VARCHAR(15)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "VARCHAR(5)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "TAXES",
                type: "VARCHAR(5)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "VARCHAR(15)");

            migrationBuilder.AlterColumn<string>(
                name: "TaxCode",
                table: "PURCHASE_CONTRACTS_TAXES",
                type: "VARCHAR(5)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "VARCHAR(15)");

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
    }
}
