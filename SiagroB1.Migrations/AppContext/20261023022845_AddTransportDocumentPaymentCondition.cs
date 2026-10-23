using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddTransportDocumentPaymentCondition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FINANCIAL_DOCUMENTS_FirmOrigin",
                table: "FINANCIAL_DOCUMENTS");

            migrationBuilder.AddColumn<int>(
                name: "PaymentConditionCode",
                table: "TRANSPORT_DOCUMENTS",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_DOCUMENTS_FirmOrigin",
                table: "FINANCIAL_DOCUMENTS",
                columns: new[] { "OriginType", "OriginKey", "SalesContractKey" },
                unique: true,
                filter: "[Nature] IN (1, 4, 5) AND [Status] <> 3 AND [OriginKey] IS NOT NULL AND [OriginType] <> 9 AND [OriginType] <> 10 AND [OriginType] <> 11");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FINANCIAL_DOCUMENTS_FirmOrigin",
                table: "FINANCIAL_DOCUMENTS");

            migrationBuilder.DropColumn(
                name: "PaymentConditionCode",
                table: "TRANSPORT_DOCUMENTS");

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_DOCUMENTS_FirmOrigin",
                table: "FINANCIAL_DOCUMENTS",
                columns: new[] { "OriginType", "OriginKey", "SalesContractKey" },
                unique: true,
                filter: "[Nature] IN (1, 4, 5) AND [Status] <> 3 AND [OriginKey] IS NOT NULL AND [OriginType] <> 9 AND [OriginType] <> 10");
        }
    }
}
