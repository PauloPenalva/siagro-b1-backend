using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddAccountingCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BlockedFrom",
                table: "LEDGER_ACCOUNTS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BlockedTo",
                table: "LEDGER_ACCOUNTS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Nature",
                table: "LEDGER_ACCOUNTS",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParentCode",
                table: "LEDGER_ACCOUNTS",
                type: "VARCHAR(20)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferentialCode",
                table: "LEDGER_ACCOUNTS",
                type: "VARCHAR(30)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultClosingAccountCode",
                table: "LEDGER_ACCOUNTS",
                type: "VARCHAR(20)",
                nullable: true);

            // Natureza das contas existentes pelo tipo (Asset=0/Expense=3 devedoras; Liability=1/Revenue=2 credoras).
            migrationBuilder.Sql(
                "UPDATE LEDGER_ACCOUNTS SET Nature = CASE WHEN Type IN (0, 3) THEN 1 ELSE 2 END " +
                "WHERE Nature IS NULL AND Type IS NOT NULL;");

            migrationBuilder.CreateTable(
                name: "ACCOUNTING_PERIODS",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateTime>(type: "DATE", nullable: false),
                    EndDate = table.Column<DateTime>(type: "DATE", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    ReopenedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReopenedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    ReopenReason = table.Column<string>(type: "VARCHAR(500)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ACCOUNTING_PERIODS", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ACCOUNTING_SETTINGS",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false),
                    CutoverDate = table.Column<DateTime>(type: "DATE", nullable: true),
                    DefaultResultClosingAccountCode = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    ClosingFrequency = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ACCOUNTING_SETTINGS", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "JOURNAL_ENTRIES",
                columns: table => new
                {
                    DocEntry = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PostingDate = table.Column<DateTime>(type: "DATE", nullable: false),
                    BranchCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    Batch = table.Column<string>(type: "VARCHAR(6)", nullable: false),
                    SubBatch = table.Column<string>(type: "VARCHAR(3)", nullable: false),
                    DocumentNumber = table.Column<string>(type: "VARCHAR(6)", nullable: false),
                    Origin = table.Column<int>(type: "int", nullable: false),
                    StandardPostingCode = table.Column<string>(type: "VARCHAR(3)", nullable: true),
                    SourceDocumentType = table.Column<string>(type: "VARCHAR(40)", nullable: true),
                    SourceDocumentKey = table.Column<string>(type: "VARCHAR(60)", nullable: true),
                    SourceEvent = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    ReversalOfDocEntry = table.Column<int>(type: "int", nullable: true),
                    ReversedByDocEntry = table.Column<int>(type: "int", nullable: true),
                    Comments = table.Column<string>(type: "VARCHAR(250)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JOURNAL_ENTRIES", x => x.DocEntry);
                });

            migrationBuilder.CreateTable(
                name: "JOURNAL_ENTRY_LINES",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocEntry = table.Column<int>(type: "int", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    EntryType = table.Column<int>(type: "int", nullable: false),
                    DebitAccountCode = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    CreditAccountCode = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(19,2)", nullable: false),
                    History = table.Column<string>(type: "VARCHAR(40)", nullable: false),
                    DebitCostCenterCode = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    CreditCostCenterCode = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    DebitAccountingItemCode = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    CreditAccountingItemCode = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    StandardPostingSequence = table.Column<string>(type: "VARCHAR(3)", nullable: true),
                    BusinessPartnerCode = table.Column<string>(type: "VARCHAR(50)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JOURNAL_ENTRY_LINES", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JOURNAL_ENTRY_LINES_JOURNAL_ENTRIES_DocEntry",
                        column: x => x.DocEntry,
                        principalTable: "JOURNAL_ENTRIES",
                        principalColumn: "DocEntry",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JOURNAL_ENTRY_LINES_LEDGER_ACCOUNTS_CreditAccountCode",
                        column: x => x.CreditAccountCode,
                        principalTable: "LEDGER_ACCOUNTS",
                        principalColumn: "Code");
                    table.ForeignKey(
                        name: "FK_JOURNAL_ENTRY_LINES_LEDGER_ACCOUNTS_DebitAccountCode",
                        column: x => x.DebitAccountCode,
                        principalTable: "LEDGER_ACCOUNTS",
                        principalColumn: "Code");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ACCOUNTING_PERIODS_Year_Month",
                table: "ACCOUNTING_PERIODS",
                columns: new[] { "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JOURNAL_ENTRIES_PostingDate_Batch_SubBatch_DocumentNumber",
                table: "JOURNAL_ENTRIES",
                columns: new[] { "PostingDate", "Batch", "SubBatch", "DocumentNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JOURNAL_ENTRIES_StandardPostingCode_SourceDocumentType_SourceDocumentKey_SourceEvent",
                table: "JOURNAL_ENTRIES",
                columns: new[] { "StandardPostingCode", "SourceDocumentType", "SourceDocumentKey", "SourceEvent" },
                unique: true,
                filter: "[SourceDocumentKey] IS NOT NULL AND [ReversalOfDocEntry] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_JOURNAL_ENTRY_LINES_CreditAccountCode",
                table: "JOURNAL_ENTRY_LINES",
                column: "CreditAccountCode")
                .Annotation("SqlServer:Include", new[] { "Amount" });

            migrationBuilder.CreateIndex(
                name: "IX_JOURNAL_ENTRY_LINES_DebitAccountCode",
                table: "JOURNAL_ENTRY_LINES",
                column: "DebitAccountCode")
                .Annotation("SqlServer:Include", new[] { "Amount" });

            migrationBuilder.CreateIndex(
                name: "IX_JOURNAL_ENTRY_LINES_DocEntry",
                table: "JOURNAL_ENTRY_LINES",
                column: "DocEntry");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ACCOUNTING_PERIODS");

            migrationBuilder.DropTable(
                name: "ACCOUNTING_SETTINGS");

            migrationBuilder.DropTable(
                name: "JOURNAL_ENTRY_LINES");

            migrationBuilder.DropTable(
                name: "JOURNAL_ENTRIES");

            migrationBuilder.DropColumn(
                name: "BlockedFrom",
                table: "LEDGER_ACCOUNTS");

            migrationBuilder.DropColumn(
                name: "BlockedTo",
                table: "LEDGER_ACCOUNTS");

            migrationBuilder.DropColumn(
                name: "Nature",
                table: "LEDGER_ACCOUNTS");

            migrationBuilder.DropColumn(
                name: "ParentCode",
                table: "LEDGER_ACCOUNTS");

            migrationBuilder.DropColumn(
                name: "ReferentialCode",
                table: "LEDGER_ACCOUNTS");

            migrationBuilder.DropColumn(
                name: "ResultClosingAccountCode",
                table: "LEDGER_ACCOUNTS");
        }
    }
}
