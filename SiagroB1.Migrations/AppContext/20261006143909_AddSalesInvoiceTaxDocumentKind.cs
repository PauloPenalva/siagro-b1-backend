using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddSalesInvoiceTaxDocumentKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TaxDocumentKind",
                table: "SALES_INVOICES",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Documento Normal já confirmado (ou cancelado/devolvido) que nunca teve NF-e pelo Siagro não é NF-e: sem
            // isto, o "Transmitir NF-e" apareceria para todo documento antigo da filial que emite. Enums: InvoiceType
            // Normal = 0, InvoiceStatus Pending = 0, NfeStatus None = 0, TaxDocumentKind Other = 1.
            migrationBuilder.Sql(
                "UPDATE SALES_INVOICES SET TaxDocumentKind = 1 WHERE InvoiceType = 0 AND InvoiceStatus <> 0 AND NfeStatus = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TaxDocumentKind",
                table: "SALES_INVOICES");
        }
    }
}
