using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddFinancialAccountReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReconciliationKey",
                table: "FINANCIAL_SETTLEMENTS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FINANCIAL_ACCOUNT_RECONCILIATIONS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FinancialAccountCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    StatementDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StatementBalance = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    OpeningBalance = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
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
                    table.PrimaryKey("PK_FINANCIAL_ACCOUNT_RECONCILIATIONS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_FINANCIAL_ACCOUNT_RECONCILIATIONS_FINANCIAL_ACCOUNTS_FinancialAccountCode",
                        column: x => x.FinancialAccountCode,
                        principalTable: "FINANCIAL_ACCOUNTS",
                        principalColumn: "Code");
                });

            migrationBuilder.CreateTable(
                name: "FINANCIAL_ACCOUNT_ENTRIES",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FinancialAccountCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    EntryDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Amount = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    Description = table.Column<string>(type: "VARCHAR(200)", nullable: false),
                    DocumentReference = table.Column<string>(type: "VARCHAR(50)", nullable: true),
                    Notes = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    ReconciliationKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_FINANCIAL_ACCOUNT_ENTRIES", x => x.Key);
                    table.ForeignKey(
                        name: "FK_FINANCIAL_ACCOUNT_ENTRIES_FINANCIAL_ACCOUNTS_FinancialAccountCode",
                        column: x => x.FinancialAccountCode,
                        principalTable: "FINANCIAL_ACCOUNTS",
                        principalColumn: "Code");
                    table.ForeignKey(
                        name: "FK_FINANCIAL_ACCOUNT_ENTRIES_FINANCIAL_ACCOUNT_RECONCILIATIONS_ReconciliationKey",
                        column: x => x.ReconciliationKey,
                        principalTable: "FINANCIAL_ACCOUNT_RECONCILIATIONS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_SETTLEMENTS_ReconciliationKey",
                table: "FINANCIAL_SETTLEMENTS",
                column: "ReconciliationKey");

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_ACCOUNT_ENTRIES_EntryDate",
                table: "FINANCIAL_ACCOUNT_ENTRIES",
                column: "EntryDate");

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_ACCOUNT_ENTRIES_FinancialAccountCode",
                table: "FINANCIAL_ACCOUNT_ENTRIES",
                column: "FinancialAccountCode");

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_ACCOUNT_ENTRIES_ReconciliationKey",
                table: "FINANCIAL_ACCOUNT_ENTRIES",
                column: "ReconciliationKey");

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_ACCOUNT_RECONCILIATIONS_FinancialAccountCode_StatementDate",
                table: "FINANCIAL_ACCOUNT_RECONCILIATIONS",
                columns: new[] { "FinancialAccountCode", "StatementDate" });

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_ACCOUNT_RECONCILIATIONS_OpenPerAccount",
                table: "FINANCIAL_ACCOUNT_RECONCILIATIONS",
                column: "FinancialAccountCode",
                unique: true,
                filter: "[Status] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_FINANCIAL_SETTLEMENTS_FINANCIAL_ACCOUNT_RECONCILIATIONS_ReconciliationKey",
                table: "FINANCIAL_SETTLEMENTS",
                column: "ReconciliationKey",
                principalTable: "FINANCIAL_ACCOUNT_RECONCILIATIONS",
                principalColumn: "Key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FINANCIAL_SETTLEMENTS_FINANCIAL_ACCOUNT_RECONCILIATIONS_ReconciliationKey",
                table: "FINANCIAL_SETTLEMENTS");

            migrationBuilder.DropTable(
                name: "FINANCIAL_ACCOUNT_ENTRIES");

            migrationBuilder.DropTable(
                name: "FINANCIAL_ACCOUNT_RECONCILIATIONS");

            migrationBuilder.DropIndex(
                name: "IX_FINANCIAL_SETTLEMENTS_ReconciliationKey",
                table: "FINANCIAL_SETTLEMENTS");

            migrationBuilder.DropColumn(
                name: "ReconciliationKey",
                table: "FINANCIAL_SETTLEMENTS");
        }
    }
}
