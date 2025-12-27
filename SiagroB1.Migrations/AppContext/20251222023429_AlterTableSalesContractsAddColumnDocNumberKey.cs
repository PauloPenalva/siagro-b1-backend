#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTableSalesContractsAddColumnDocNumberKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DocNumberKey",
                table: "SALES_CONTRACTS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SALES_CONTRACTS_DocNumberKey",
                table: "SALES_CONTRACTS",
                column: "DocNumberKey");

            migrationBuilder.AddForeignKey(
                name: "FK_SALES_CONTRACTS_DOC_NUMBERS_DocNumberKey",
                table: "SALES_CONTRACTS",
                column: "DocNumberKey",
                principalTable: "DOC_NUMBERS",
                principalColumn: "Key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SALES_CONTRACTS_DOC_NUMBERS_DocNumberKey",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropIndex(
                name: "IX_SALES_CONTRACTS_DocNumberKey",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "DocNumberKey",
                table: "SALES_CONTRACTS");
        }
    }
}
