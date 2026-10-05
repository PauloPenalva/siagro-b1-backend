using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddPurchaseInvoiceTaxDocumentKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SupplierNfeCheckedAt",
                table: "PURCHASE_INVOICES",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupplierNfeProtocol",
                table: "PURCHASE_INVOICES",
                type: "VARCHAR(20)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TaxDocumentKind",
                table: "PURCHASE_INVOICES",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Documento antigo de terceiro sem chave de 44 caracteres não é eletrônico: não passa a exigir chave.
            migrationBuilder.Sql(
                "UPDATE PURCHASE_INVOICES SET TaxDocumentKind = 1 WHERE IssuerType = 0 AND (ChaveNFe IS NULL OR LEN(ChaveNFe) <> 44)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SupplierNfeCheckedAt",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "SupplierNfeProtocol",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "TaxDocumentKind",
                table: "PURCHASE_INVOICES");
        }
    }
}
