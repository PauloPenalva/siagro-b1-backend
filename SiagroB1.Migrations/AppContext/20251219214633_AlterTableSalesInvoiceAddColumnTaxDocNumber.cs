#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTableSalesInvoiceAddColumnTaxDocNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChaveNFe",
                table: "SALES_INVOICES",
                type: "VARCHAR(44)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryCardCode",
                table: "SALES_INVOICES",
                type: "VARCHAR(15)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryCardName",
                table: "SALES_INVOICES",
                type: "VARCHAR(200)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FreightCostStandard",
                table: "SALES_INVOICES",
                type: "DECIMAL(18,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "FreightTerms",
                table: "SALES_INVOICES",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TaxComments",
                table: "SALES_INVOICES",
                type: "VARCHAR(500)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaxDocumentNumber",
                table: "SALES_INVOICES",
                type: "VARCHAR(9)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaxDocumentSeries",
                table: "SALES_INVOICES",
                type: "VARCHAR(3)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaxPayerComments",
                table: "SALES_INVOICES",
                type: "VARCHAR(500)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChaveNFe",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "DeliveryCardCode",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "DeliveryCardName",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "FreightCostStandard",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "FreightTerms",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "TaxComments",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "TaxDocumentNumber",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "TaxDocumentSeries",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "TaxPayerComments",
                table: "SALES_INVOICES");
        }
    }
}
