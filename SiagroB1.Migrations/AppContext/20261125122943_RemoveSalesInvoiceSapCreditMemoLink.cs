using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class RemoveSalesInvoiceSapCreditMemoLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SALES_INVOICES_SapCreditMemoDocEntry",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "SapCreditMemoDocEntry",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "SapCreditMemoDocNum",
                table: "SALES_INVOICES");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SapCreditMemoDocEntry",
                table: "SALES_INVOICES",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SapCreditMemoDocNum",
                table: "SALES_INVOICES",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SALES_INVOICES_SapCreditMemoDocEntry",
                table: "SALES_INVOICES",
                column: "SapCreditMemoDocEntry",
                unique: true,
                filter: "[SapCreditMemoDocEntry] IS NOT NULL AND [InvoiceStatus] <> 2 AND [InvoiceStatus] <> 3");
        }
    }
}
