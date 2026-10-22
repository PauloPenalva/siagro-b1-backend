using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddCostCenterAccountingItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "CostCenterCode",
                table: "SALES_INVOICES_ITEMS",
                type: "VARCHAR(20)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "VARCHAR(10)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccountingItemCode",
                table: "SALES_INVOICES_ITEMS",
                type: "VARCHAR(20)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DistributionRuleCode",
                table: "SALES_INVOICES_ITEMS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccountingItemCode",
                table: "SALES_INVOICES",
                type: "VARCHAR(20)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CostCenterCode",
                table: "SALES_INVOICES",
                type: "VARCHAR(20)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DistributionRuleCode",
                table: "SALES_INVOICES",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LedgerAccountCode",
                table: "SALES_INVOICES",
                type: "VARCHAR(20)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccountingItemCode",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "VARCHAR(20)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CostCenterCode",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "VARCHAR(20)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DistributionRuleCode",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LedgerAccountCode",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "VARCHAR(20)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccountingItemCode",
                table: "PURCHASE_INVOICES",
                type: "VARCHAR(20)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CostCenterCode",
                table: "PURCHASE_INVOICES",
                type: "VARCHAR(20)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DistributionRuleCode",
                table: "PURCHASE_INVOICES",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LedgerAccountCode",
                table: "PURCHASE_INVOICES",
                type: "VARCHAR(20)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AcceptsAccountingItem",
                table: "LEDGER_ACCOUNTS",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "AcceptsCostCenter",
                table: "LEDGER_ACCOUNTS",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresAccountingItem",
                table: "LEDGER_ACCOUNTS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresCostCenter",
                table: "LEDGER_ACCOUNTS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AccountingItemCode",
                table: "FINANCIAL_DOCUMENTS",
                type: "VARCHAR(20)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CostCenterCode",
                table: "FINANCIAL_DOCUMENTS",
                type: "VARCHAR(20)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DistributionRuleCode",
                table: "FINANCIAL_DOCUMENTS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LedgerAccountCode",
                table: "FINANCIAL_DOCUMENTS",
                type: "VARCHAR(20)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccountingItemCode",
                table: "FINANCIAL_ACCOUNT_ENTRIES",
                type: "VARCHAR(20)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CostCenterCode",
                table: "FINANCIAL_ACCOUNT_ENTRIES",
                type: "VARCHAR(20)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DistributionRuleCode",
                table: "FINANCIAL_ACCOUNT_ENTRIES",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LedgerAccountCode",
                table: "FINANCIAL_ACCOUNT_ENTRIES",
                type: "VARCHAR(20)",
                nullable: true);

            // Code é a PK: o SQL Server não altera coluna usada por índice, então a PK sai e volta.
            migrationBuilder.DropPrimaryKey(name: "PK_COST_CENTERS", table: "COST_CENTERS");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "COST_CENTERS",
                type: "VARCHAR(20)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "VARCHAR(10)");

            migrationBuilder.AddPrimaryKey(name: "PK_COST_CENTERS", table: "COST_CENTERS", column: "Code");

            migrationBuilder.AddColumn<int>(
                name: "Class",
                table: "COST_CENTERS",
                type: "int",
                nullable: false,
                defaultValue: 2); // Centros existentes migram como analíticos (AccountingEntityClass.Analytic), sem pai.

            migrationBuilder.AddColumn<string>(
                name: "ParentCode",
                table: "COST_CENTERS",
                type: "VARCHAR(20)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ValidFrom",
                table: "COST_CENTERS",
                type: "DATE",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ValidTo",
                table: "COST_CENTERS",
                type: "DATE",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultAccountingItemCode",
                table: "BUSINESS_PARTNERS",
                type: "VARCHAR(20)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultLedgerAccountCode",
                table: "BUSINESS_PARTNERS",
                type: "VARCHAR(20)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ACCOUNTING_ITEMS",
                columns: table => new
                {
                    Code = table.Column<string>(type: "VARCHAR(20)", nullable: false),
                    Name = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    Class = table.Column<int>(type: "int", nullable: false),
                    ParentCode = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    Inactive = table.Column<bool>(type: "bit", nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "DATE", nullable: true),
                    ValidTo = table.Column<DateTime>(type: "DATE", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ACCOUNTING_ITEMS", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "DISTRIBUTION_RULES",
                columns: table => new
                {
                    Code = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    Name = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    Inactive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DISTRIBUTION_RULES", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "DISTRIBUTION_RULE_VERSIONS",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "DATE", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DISTRIBUTION_RULE_VERSIONS", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DISTRIBUTION_RULE_VERSIONS_DISTRIBUTION_RULES_RuleCode",
                        column: x => x.RuleCode,
                        principalTable: "DISTRIBUTION_RULES",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DISTRIBUTION_RULE_LINES",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CostCenterCode = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    AccountingItemCode = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    Percentage = table.Column<decimal>(type: "DECIMAL(9,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DISTRIBUTION_RULE_LINES", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DISTRIBUTION_RULE_LINES_DISTRIBUTION_RULE_VERSIONS_VersionId",
                        column: x => x.VersionId,
                        principalTable: "DISTRIBUTION_RULE_VERSIONS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DISTRIBUTION_RULE_LINES_VersionId",
                table: "DISTRIBUTION_RULE_LINES",
                column: "VersionId");

            migrationBuilder.CreateIndex(
                name: "IX_DISTRIBUTION_RULE_VERSIONS_RuleCode_ValidFrom",
                table: "DISTRIBUTION_RULE_VERSIONS",
                columns: new[] { "RuleCode", "ValidFrom" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ACCOUNTING_ITEMS");

            migrationBuilder.DropTable(
                name: "DISTRIBUTION_RULE_LINES");

            migrationBuilder.DropTable(
                name: "DISTRIBUTION_RULE_VERSIONS");

            migrationBuilder.DropTable(
                name: "DISTRIBUTION_RULES");

            migrationBuilder.DropColumn(
                name: "AccountingItemCode",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "DistributionRuleCode",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "AccountingItemCode",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "CostCenterCode",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "DistributionRuleCode",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "LedgerAccountCode",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "AccountingItemCode",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "CostCenterCode",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "DistributionRuleCode",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "LedgerAccountCode",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "AccountingItemCode",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "CostCenterCode",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "DistributionRuleCode",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "LedgerAccountCode",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "AcceptsAccountingItem",
                table: "LEDGER_ACCOUNTS");

            migrationBuilder.DropColumn(
                name: "AcceptsCostCenter",
                table: "LEDGER_ACCOUNTS");

            migrationBuilder.DropColumn(
                name: "RequiresAccountingItem",
                table: "LEDGER_ACCOUNTS");

            migrationBuilder.DropColumn(
                name: "RequiresCostCenter",
                table: "LEDGER_ACCOUNTS");

            migrationBuilder.DropColumn(
                name: "AccountingItemCode",
                table: "FINANCIAL_DOCUMENTS");

            migrationBuilder.DropColumn(
                name: "CostCenterCode",
                table: "FINANCIAL_DOCUMENTS");

            migrationBuilder.DropColumn(
                name: "DistributionRuleCode",
                table: "FINANCIAL_DOCUMENTS");

            migrationBuilder.DropColumn(
                name: "LedgerAccountCode",
                table: "FINANCIAL_DOCUMENTS");

            migrationBuilder.DropColumn(
                name: "AccountingItemCode",
                table: "FINANCIAL_ACCOUNT_ENTRIES");

            migrationBuilder.DropColumn(
                name: "CostCenterCode",
                table: "FINANCIAL_ACCOUNT_ENTRIES");

            migrationBuilder.DropColumn(
                name: "DistributionRuleCode",
                table: "FINANCIAL_ACCOUNT_ENTRIES");

            migrationBuilder.DropColumn(
                name: "LedgerAccountCode",
                table: "FINANCIAL_ACCOUNT_ENTRIES");

            migrationBuilder.DropColumn(
                name: "Class",
                table: "COST_CENTERS");

            migrationBuilder.DropColumn(
                name: "ParentCode",
                table: "COST_CENTERS");

            migrationBuilder.DropColumn(
                name: "ValidFrom",
                table: "COST_CENTERS");

            migrationBuilder.DropColumn(
                name: "ValidTo",
                table: "COST_CENTERS");

            migrationBuilder.DropColumn(
                name: "DefaultAccountingItemCode",
                table: "BUSINESS_PARTNERS");

            migrationBuilder.DropColumn(
                name: "DefaultLedgerAccountCode",
                table: "BUSINESS_PARTNERS");

            migrationBuilder.AlterColumn<string>(
                name: "CostCenterCode",
                table: "SALES_INVOICES_ITEMS",
                type: "VARCHAR(10)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "VARCHAR(20)",
                oldNullable: true);

            migrationBuilder.DropPrimaryKey(name: "PK_COST_CENTERS", table: "COST_CENTERS");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "COST_CENTERS",
                type: "VARCHAR(10)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "VARCHAR(20)");

            migrationBuilder.AddPrimaryKey(name: "PK_COST_CENTERS", table: "COST_CENTERS", column: "Code");
        }
    }
}
