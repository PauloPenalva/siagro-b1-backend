using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class OtherPurchaseTitlesUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_DOCUMENTS_OtherPurchaseOrigin",
                table: "FINANCIAL_DOCUMENTS",
                columns: new[] { "OriginType", "OriginKey", "InstallmentNumber", "WithholdingTaxType" },
                unique: true,
                filter: "[OriginType] = 9 AND [Status] <> 3");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FINANCIAL_DOCUMENTS_OtherPurchaseOrigin",
                table: "FINANCIAL_DOCUMENTS");
        }
    }
}
