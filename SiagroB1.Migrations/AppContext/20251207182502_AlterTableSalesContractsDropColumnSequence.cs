#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTableSalesContractsDropColumnSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SALES_CONTRACTS_Code_Sequence",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "FreightCost",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "Sequence",
                table: "SALES_CONTRACTS");

            migrationBuilder.AddColumn<string>(
                name: "DocTypeCode",
                table: "SALES_CONTRACTS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SALES_CONTRACTS_Code",
                table: "SALES_CONTRACTS",
                column: "Code",
                unique: true);

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SALES_CONTRACTS_DOC_TYPES_DocTypeCode",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropIndex(
                name: "IX_SALES_CONTRACTS_Code",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropIndex(
                name: "IX_SALES_CONTRACTS_DocTypeCode",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "DocTypeCode",
                table: "SALES_CONTRACTS");

            migrationBuilder.AddColumn<decimal>(
                name: "FreightCost",
                table: "SALES_CONTRACTS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Sequence",
                table: "SALES_CONTRACTS",
                type: "VARCHAR(3)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_CONTRACTS_Code_Sequence",
                table: "SALES_CONTRACTS",
                columns: new[] { "Code", "Sequence" },
                unique: true);
        }
    }
}
