#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterDocumentTablesAddColumnBranchCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BranchCode",
                table: "STORAGE_TRANSACTIONS",
                type: "VARCHAR(14)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BranchCode",
                table: "SALES_INVOICES",
                type: "VARCHAR(14)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BranchCode",
                table: "SALES_CONTRACTS",
                type: "VARCHAR(14)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BranchCode",
                table: "PURCHASE_CONTRACTS",
                type: "VARCHAR(14)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BranchCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "BranchCode",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "BranchCode",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "BranchCode",
                table: "PURCHASE_CONTRACTS");
        }
    }
}
