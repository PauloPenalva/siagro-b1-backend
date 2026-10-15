using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddSalesFirmReceivables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FINANCIAL_DOCUMENTS_FirmOrigin",
                table: "FINANCIAL_DOCUMENTS");

            migrationBuilder.AddColumn<Guid>(
                name: "SalesInvoiceKey",
                table: "FINANCIAL_DOCUMENTS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_DOCUMENTS_FirmOrigin",
                table: "FINANCIAL_DOCUMENTS",
                columns: new[] { "OriginType", "OriginKey", "SalesContractKey" },
                unique: true,
                filter: "[Nature] IN (1, 4, 5) AND [Status] <> 3 AND [OriginKey] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FINANCIAL_DOCUMENTS_FirmOrigin",
                table: "FINANCIAL_DOCUMENTS");

            migrationBuilder.DropColumn(
                name: "SalesInvoiceKey",
                table: "FINANCIAL_DOCUMENTS");

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_DOCUMENTS_FirmOrigin",
                table: "FINANCIAL_DOCUMENTS",
                columns: new[] { "OriginType", "OriginKey" },
                unique: true,
                filter: "[Nature] IN (1, 4) AND [Status] <> 3 AND [OriginKey] IS NOT NULL");
        }
    }
}
