using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class CreateFinancialModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FINANCIAL_ACCOUNTS",
                columns: table => new
                {
                    Code = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    Name = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    BankCode = table.Column<string>(type: "VARCHAR(3)", nullable: true),
                    BankName = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    BankBranch = table.Column<string>(type: "VARCHAR(10)", nullable: true),
                    BankAccountNumber = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    Currency = table.Column<int>(type: "INT DEFAULT 1", nullable: false),
                    BranchCode = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    Inactive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FINANCIAL_ACCOUNTS", x => x.Code);
                    table.ForeignKey(
                        name: "FK_FINANCIAL_ACCOUNTS_BRANCHS_BranchCode",
                        column: x => x.BranchCode,
                        principalTable: "BRANCHS",
                        principalColumn: "Code");
                });

            migrationBuilder.CreateTable(
                name: "FINANCIAL_DOCUMENTS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "VARCHAR(50)", nullable: true),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    Nature = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CardCode = table.Column<string>(type: "VARCHAR(15)", nullable: false),
                    CardName = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    DocumentDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Currency = table.Column<int>(type: "INT DEFAULT 1", nullable: false),
                    NetAmount = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    SettledAmount = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    OriginType = table.Column<int>(type: "int", nullable: false),
                    OriginKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OriginDocNumber = table.Column<string>(type: "VARCHAR(50)", nullable: true),
                    PurchaseContractKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SalesContractKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PaymentTermsText = table.Column<string>(type: "VARCHAR(1000)", nullable: true),
                    Comments = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    CancellationReason = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    RowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CanceledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CanceledBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    DocNumberKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BranchCode = table.Column<string>(type: "VARCHAR(14)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FINANCIAL_DOCUMENTS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_FINANCIAL_DOCUMENTS_BRANCHS_BranchCode",
                        column: x => x.BranchCode,
                        principalTable: "BRANCHS",
                        principalColumn: "Code");
                    table.ForeignKey(
                        name: "FK_FINANCIAL_DOCUMENTS_DOC_NUMBERS_DocNumberKey",
                        column: x => x.DocNumberKey,
                        principalTable: "DOC_NUMBERS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_FINANCIAL_DOCUMENTS_PURCHASE_CONTRACTS_PurchaseContractKey",
                        column: x => x.PurchaseContractKey,
                        principalTable: "PURCHASE_CONTRACTS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_FINANCIAL_DOCUMENTS_SALES_CONTRACTS_SalesContractKey",
                        column: x => x.SalesContractKey,
                        principalTable: "SALES_CONTRACTS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateTable(
                name: "FINANCIAL_DOCUMENT_CHANGE_LOGS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FinancialDocumentKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChangedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    Field = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    OldValue = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    NewValue = table.Column<string>(type: "VARCHAR(500)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FINANCIAL_DOCUMENT_CHANGE_LOGS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_FINANCIAL_DOCUMENT_CHANGE_LOGS_FINANCIAL_DOCUMENTS_FinancialDocumentKey",
                        column: x => x.FinancialDocumentKey,
                        principalTable: "FINANCIAL_DOCUMENTS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateTable(
                name: "FINANCIAL_SETTLEMENTS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FinancialDocumentKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FinancialAccountCode = table.Column<string>(type: "VARCHAR(10)", nullable: true),
                    SettlementDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Amount = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    InterestAmount = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    FineAmount = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    Origin = table.Column<int>(type: "int", nullable: false),
                    ReversedSettlementKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DocumentReference = table.Column<string>(type: "VARCHAR(50)", nullable: true),
                    Notes = table.Column<string>(type: "VARCHAR(500)", nullable: true),
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
                    table.PrimaryKey("PK_FINANCIAL_SETTLEMENTS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_FINANCIAL_SETTLEMENTS_FINANCIAL_ACCOUNTS_FinancialAccountCode",
                        column: x => x.FinancialAccountCode,
                        principalTable: "FINANCIAL_ACCOUNTS",
                        principalColumn: "Code");
                    table.ForeignKey(
                        name: "FK_FINANCIAL_SETTLEMENTS_FINANCIAL_DOCUMENTS_FinancialDocumentKey",
                        column: x => x.FinancialDocumentKey,
                        principalTable: "FINANCIAL_DOCUMENTS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_ACCOUNTS_BranchCode",
                table: "FINANCIAL_ACCOUNTS",
                column: "BranchCode");

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_DOCUMENT_CHANGE_LOGS_FinancialDocumentKey",
                table: "FINANCIAL_DOCUMENT_CHANGE_LOGS",
                column: "FinancialDocumentKey");

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_DOCUMENTS_BranchCode",
                table: "FINANCIAL_DOCUMENTS",
                column: "BranchCode");

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_DOCUMENTS_CardCode",
                table: "FINANCIAL_DOCUMENTS",
                column: "CardCode");

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_DOCUMENTS_Code",
                table: "FINANCIAL_DOCUMENTS",
                column: "Code",
                unique: true,
                filter: "[Code] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_DOCUMENTS_Direction_Status_DueDate",
                table: "FINANCIAL_DOCUMENTS",
                columns: new[] { "Direction", "Status", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_DOCUMENTS_DocNumberKey",
                table: "FINANCIAL_DOCUMENTS",
                column: "DocNumberKey");

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_DOCUMENTS_ProvisionalOrigin",
                table: "FINANCIAL_DOCUMENTS",
                columns: new[] { "OriginType", "OriginKey" },
                unique: true,
                filter: "[Nature] = 0 AND [Status] <> 3 AND [OriginKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_DOCUMENTS_PurchaseContractKey",
                table: "FINANCIAL_DOCUMENTS",
                column: "PurchaseContractKey");

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_DOCUMENTS_SalesContractKey",
                table: "FINANCIAL_DOCUMENTS",
                column: "SalesContractKey");

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_SETTLEMENTS_FinancialAccountCode",
                table: "FINANCIAL_SETTLEMENTS",
                column: "FinancialAccountCode");

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_SETTLEMENTS_FinancialDocumentKey",
                table: "FINANCIAL_SETTLEMENTS",
                column: "FinancialDocumentKey");

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_SETTLEMENTS_ReversedSettlementKey",
                table: "FINANCIAL_SETTLEMENTS",
                column: "ReversedSettlementKey",
                unique: true,
                filter: "[ReversedSettlementKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_SETTLEMENTS_SettlementDate",
                table: "FINANCIAL_SETTLEMENTS",
                column: "SettlementDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FINANCIAL_DOCUMENT_CHANGE_LOGS");

            migrationBuilder.DropTable(
                name: "FINANCIAL_SETTLEMENTS");

            migrationBuilder.DropTable(
                name: "FINANCIAL_ACCOUNTS");

            migrationBuilder.DropTable(
                name: "FINANCIAL_DOCUMENTS");
        }
    }
}
