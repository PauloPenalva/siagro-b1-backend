using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddFreightInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FINANCIAL_DOCUMENTS_FirmOrigin",
                table: "FINANCIAL_DOCUMENTS");

            migrationBuilder.CreateTable(
                name: "FREIGHT_INVOICES",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchCode = table.Column<string>(type: "VARCHAR(14)", nullable: false),
                    DocNumberKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "VARCHAR(20)", nullable: false),
                    TakerCardCode = table.Column<string>(type: "VARCHAR(15)", nullable: false),
                    TakerName = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    IssueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PaymentConditionCode = table.Column<int>(type: "int", nullable: true),
                    TotalAmount = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    Observation = table.Column<string>(type: "VARCHAR(2000)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FREIGHT_INVOICES", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "FREIGHT_INVOICE_ITEMS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransportDocumentKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNum = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FREIGHT_INVOICE_ITEMS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_FREIGHT_INVOICE_ITEMS_FREIGHT_INVOICES_InvoiceKey",
                        column: x => x.InvoiceKey,
                        principalTable: "FREIGHT_INVOICES",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FREIGHT_INVOICE_ITEMS_TRANSPORT_DOCUMENTS_TransportDocumentKey",
                        column: x => x.TransportDocumentKey,
                        principalTable: "TRANSPORT_DOCUMENTS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_DOCUMENTS_FirmOrigin",
                table: "FINANCIAL_DOCUMENTS",
                columns: new[] { "OriginType", "OriginKey", "SalesContractKey" },
                unique: true,
                filter: "[Nature] IN (1, 4, 5) AND [Status] <> 3 AND [OriginKey] IS NOT NULL AND [OriginType] <> 9 AND [OriginType] <> 10 AND [OriginType] <> 11 AND [OriginType] <> 12");

            migrationBuilder.CreateIndex(
                name: "IX_FREIGHT_INVOICE_ITEMS_InvoiceKey",
                table: "FREIGHT_INVOICE_ITEMS",
                column: "InvoiceKey");

            migrationBuilder.CreateIndex(
                name: "IX_FREIGHT_INVOICE_ITEMS_TransportDocumentKey",
                table: "FREIGHT_INVOICE_ITEMS",
                column: "TransportDocumentKey",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_FREIGHT_INVOICES_BranchCode_Code",
                table: "FREIGHT_INVOICES",
                columns: new[] { "BranchCode", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FREIGHT_INVOICE_ITEMS");

            migrationBuilder.DropTable(
                name: "FREIGHT_INVOICES");

            migrationBuilder.DropIndex(
                name: "IX_FINANCIAL_DOCUMENTS_FirmOrigin",
                table: "FINANCIAL_DOCUMENTS");

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_DOCUMENTS_FirmOrigin",
                table: "FINANCIAL_DOCUMENTS",
                columns: new[] { "OriginType", "OriginKey", "SalesContractKey" },
                unique: true,
                filter: "[Nature] IN (1, 4, 5) AND [Status] <> 3 AND [OriginKey] IS NOT NULL AND [OriginType] <> 9 AND [OriginType] <> 10 AND [OriginType] <> 11");
        }
    }
}
