#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTableSalesInvoicesAddColumnDocTypeCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DocTypeCode",
                table: "SALES_INVOICES",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SALES_INVOICES_DocTypeCode",
                table: "SALES_INVOICES",
                column: "DocTypeCode");

            migrationBuilder.AddForeignKey(
                name: "FK_SALES_INVOICES_DOC_TYPES_DocTypeCode",
                table: "SALES_INVOICES",
                column: "DocTypeCode",
                principalTable: "DOC_TYPES",
                principalColumn: "Code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SALES_INVOICES_DOC_TYPES_DocTypeCode",
                table: "SALES_INVOICES");

            migrationBuilder.DropIndex(
                name: "IX_SALES_INVOICES_DocTypeCode",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "DocTypeCode",
                table: "SALES_INVOICES");
        }
    }
}
