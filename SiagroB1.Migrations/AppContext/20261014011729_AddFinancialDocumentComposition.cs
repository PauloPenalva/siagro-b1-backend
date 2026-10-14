using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddFinancialDocumentComposition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CompositionKey",
                table: "FINANCIAL_SETTLEMENTS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RelatedDocumentKey",
                table: "FINANCIAL_SETTLEMENTS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ComposedAmount",
                table: "FINANCIAL_DOCUMENTS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseInvoiceKey",
                table: "FINANCIAL_DOCUMENTS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageTransactionCode",
                table: "FINANCIAL_DOCUMENTS",
                type: "VARCHAR(50)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StorageTransactionKey",
                table: "FINANCIAL_DOCUMENTS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaxDocumentNumber",
                table: "FINANCIAL_DOCUMENTS",
                type: "VARCHAR(50)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FINANCIAL_DOCUMENT_COMPOSITIONS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProvisionalDocumentKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComposedDocumentKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    RowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CanceledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CanceledBy = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FINANCIAL_DOCUMENT_COMPOSITIONS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_FINANCIAL_DOCUMENT_COMPOSITIONS_FINANCIAL_DOCUMENTS_ComposedDocumentKey",
                        column: x => x.ComposedDocumentKey,
                        principalTable: "FINANCIAL_DOCUMENTS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_FINANCIAL_DOCUMENT_COMPOSITIONS_FINANCIAL_DOCUMENTS_ProvisionalDocumentKey",
                        column: x => x.ProvisionalDocumentKey,
                        principalTable: "FINANCIAL_DOCUMENTS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_DOCUMENTS_FirmOrigin",
                table: "FINANCIAL_DOCUMENTS",
                columns: new[] { "OriginType", "OriginKey" },
                unique: true,
                filter: "[Nature] IN (1, 4) AND [Status] <> 3 AND [OriginKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_DOCUMENT_COMPOSITIONS_ComposedDocumentKey",
                table: "FINANCIAL_DOCUMENT_COMPOSITIONS",
                column: "ComposedDocumentKey");

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_DOCUMENT_COMPOSITIONS_ProvisionalDocumentKey",
                table: "FINANCIAL_DOCUMENT_COMPOSITIONS",
                column: "ProvisionalDocumentKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FINANCIAL_DOCUMENT_COMPOSITIONS");

            migrationBuilder.DropIndex(
                name: "IX_FINANCIAL_DOCUMENTS_FirmOrigin",
                table: "FINANCIAL_DOCUMENTS");

            migrationBuilder.DropColumn(
                name: "CompositionKey",
                table: "FINANCIAL_SETTLEMENTS");

            migrationBuilder.DropColumn(
                name: "RelatedDocumentKey",
                table: "FINANCIAL_SETTLEMENTS");

            migrationBuilder.DropColumn(
                name: "ComposedAmount",
                table: "FINANCIAL_DOCUMENTS");

            migrationBuilder.DropColumn(
                name: "PurchaseInvoiceKey",
                table: "FINANCIAL_DOCUMENTS");

            migrationBuilder.DropColumn(
                name: "StorageTransactionCode",
                table: "FINANCIAL_DOCUMENTS");

            migrationBuilder.DropColumn(
                name: "StorageTransactionKey",
                table: "FINANCIAL_DOCUMENTS");

            migrationBuilder.DropColumn(
                name: "TaxDocumentNumber",
                table: "FINANCIAL_DOCUMENTS");
        }
    }
}
