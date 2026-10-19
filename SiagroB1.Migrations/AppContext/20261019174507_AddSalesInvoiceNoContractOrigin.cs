using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddSalesInvoiceNoContractOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FINANCIAL_DOCUMENTS_FirmOrigin",
                table: "FINANCIAL_DOCUMENTS");

            migrationBuilder.DropIndex(
                name: "IX_FINANCIAL_DOCUMENTS_OtherPurchaseOrigin",
                table: "FINANCIAL_DOCUMENTS");

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_DOCUMENTS_FirmOrigin",
                table: "FINANCIAL_DOCUMENTS",
                columns: new[] { "OriginType", "OriginKey", "SalesContractKey" },
                unique: true,
                filter: "[Nature] IN (1, 4, 5) AND [Status] <> 3 AND [OriginKey] IS NOT NULL AND [OriginType] <> 9 AND [OriginType] <> 10");

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_DOCUMENTS_OtherPurchaseOrigin",
                table: "FINANCIAL_DOCUMENTS",
                columns: new[] { "OriginType", "OriginKey", "InstallmentNumber", "WithholdingTaxType" },
                unique: true,
                filter: "[OriginType] IN (9, 10) AND [Status] <> 3");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FINANCIAL_DOCUMENTS_FirmOrigin",
                table: "FINANCIAL_DOCUMENTS");

            migrationBuilder.DropIndex(
                name: "IX_FINANCIAL_DOCUMENTS_OtherPurchaseOrigin",
                table: "FINANCIAL_DOCUMENTS");

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_DOCUMENTS_FirmOrigin",
                table: "FINANCIAL_DOCUMENTS",
                columns: new[] { "OriginType", "OriginKey", "SalesContractKey" },
                unique: true,
                filter: "[Nature] IN (1, 4, 5) AND [Status] <> 3 AND [OriginKey] IS NOT NULL AND [OriginType] <> 9");

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_DOCUMENTS_OtherPurchaseOrigin",
                table: "FINANCIAL_DOCUMENTS",
                columns: new[] { "OriginType", "OriginKey", "InstallmentNumber", "WithholdingTaxType" },
                unique: true,
                filter: "[OriginType] = 9 AND [Status] <> 3");
        }
    }
}
