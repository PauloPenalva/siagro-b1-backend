using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AlterTableSalesContractsDropColumnDocTypeCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SALES_CONTRACTS_DOC_TYPES_DocTypeCode",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropIndex(
                name: "IX_SALES_CONTRACTS_DocTypeCode",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "DocTypeCode",
                table: "SALES_CONTRACTS");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DocTypeCode",
                table: "SALES_CONTRACTS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SALES_CONTRACTS_DocTypeCode",
                table: "SALES_CONTRACTS",
                column: "DocTypeCode");

            migrationBuilder.AddForeignKey(
                name: "FK_SALES_CONTRACTS_DOC_TYPES_DocTypeCode",
                table: "SALES_CONTRACTS",
                column: "DocTypeCode",
                principalTable: "DOC_TYPES",
                principalColumn: "Code");
        }
    }
}
