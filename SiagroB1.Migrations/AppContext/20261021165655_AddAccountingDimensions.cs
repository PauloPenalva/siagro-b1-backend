using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddAccountingDimensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "CostCenterCode",
                table: "SALES_INVOICES_ITEMS",
                newName: "Dim1Code");

            migrationBuilder.AddColumn<string>(
                name: "Dim1Code",
                table: "USAGES",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Dim2Code",
                table: "USAGES",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Dim3Code",
                table: "USAGES",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Dim4Code",
                table: "USAGES",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Dim5Code",
                table: "USAGES",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LedgerAccountCode",
                table: "USAGES",
                type: "VARCHAR(20)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Dim2Code",
                table: "SALES_INVOICES_ITEMS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Dim3Code",
                table: "SALES_INVOICES_ITEMS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Dim4Code",
                table: "SALES_INVOICES_ITEMS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Dim5Code",
                table: "SALES_INVOICES_ITEMS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Dim1Code",
                table: "SALES_INVOICES",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Dim2Code",
                table: "SALES_INVOICES",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Dim3Code",
                table: "SALES_INVOICES",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Dim4Code",
                table: "SALES_INVOICES",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Dim5Code",
                table: "SALES_INVOICES",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LedgerAccountCode",
                table: "SALES_INVOICES",
                type: "VARCHAR(20)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Dim1Code",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Dim2Code",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Dim3Code",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Dim4Code",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Dim5Code",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LedgerAccountCode",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "VARCHAR(20)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Dim1Code",
                table: "PURCHASE_INVOICES",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Dim2Code",
                table: "PURCHASE_INVOICES",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Dim3Code",
                table: "PURCHASE_INVOICES",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Dim4Code",
                table: "PURCHASE_INVOICES",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Dim5Code",
                table: "PURCHASE_INVOICES",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LedgerAccountCode",
                table: "PURCHASE_INVOICES",
                type: "VARCHAR(20)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Dim1Code",
                table: "FINANCIAL_DOCUMENTS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Dim2Code",
                table: "FINANCIAL_DOCUMENTS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Dim3Code",
                table: "FINANCIAL_DOCUMENTS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Dim4Code",
                table: "FINANCIAL_DOCUMENTS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Dim5Code",
                table: "FINANCIAL_DOCUMENTS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LedgerAccountCode",
                table: "FINANCIAL_DOCUMENTS",
                type: "VARCHAR(20)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Dim1Code",
                table: "FINANCIAL_ACCOUNT_ENTRIES",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Dim2Code",
                table: "FINANCIAL_ACCOUNT_ENTRIES",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Dim3Code",
                table: "FINANCIAL_ACCOUNT_ENTRIES",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Dim4Code",
                table: "FINANCIAL_ACCOUNT_ENTRIES",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Dim5Code",
                table: "FINANCIAL_ACCOUNT_ENTRIES",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LedgerAccountCode",
                table: "FINANCIAL_ACCOUNT_ENTRIES",
                type: "VARCHAR(20)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DimensionNumber",
                table: "COST_CENTERS",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "DefaultDimensionCode",
                table: "BUSINESS_PARTNERS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ACCOUNTING_DIMENSIONS",
                columns: table => new
                {
                    Number = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ACCOUNTING_DIMENSIONS", x => x.Number);
                });

            migrationBuilder.CreateTable(
                name: "COMPANY_SETTINGS",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    RequireCostCenter = table.Column<bool>(type: "bit", nullable: false),
                    RequireLedgerAccount = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_COMPANY_SETTINGS", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DISTRIBUTION_RULES",
                columns: table => new
                {
                    Code = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    Name = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    DimensionNumber = table.Column<int>(type: "int", nullable: false),
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
                    CostCenterCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
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

            migrationBuilder.InsertData(
                table: "ACCOUNTING_DIMENSIONS",
                columns: new[] { "Number", "Active", "Name", "Role" },
                values: new object[,]
                {
                    { 1, true, "Dimensão 1", 0 },
                    { 2, false, "Dimensão 2", 0 },
                    { 3, false, "Dimensão 3", 0 },
                    { 4, false, "Dimensão 4", 0 },
                    { 5, false, "Dimensão 5", 0 }
                });

            migrationBuilder.InsertData(
                table: "COMPANY_SETTINGS",
                columns: new[] { "Id", "RequireCostCenter", "RequireLedgerAccount" },
                values: new object[] { 1, false, false });

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
                name: "ACCOUNTING_DIMENSIONS");

            migrationBuilder.DropTable(
                name: "COMPANY_SETTINGS");

            migrationBuilder.DropTable(
                name: "DISTRIBUTION_RULE_LINES");

            migrationBuilder.DropTable(
                name: "DISTRIBUTION_RULE_VERSIONS");

            migrationBuilder.DropTable(
                name: "DISTRIBUTION_RULES");

            migrationBuilder.DropColumn(
                name: "Dim1Code",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "Dim2Code",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "Dim3Code",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "Dim4Code",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "Dim5Code",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "LedgerAccountCode",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "Dim2Code",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "Dim3Code",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "Dim4Code",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "Dim5Code",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "Dim1Code",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "Dim2Code",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "Dim3Code",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "Dim4Code",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "Dim5Code",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "LedgerAccountCode",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "Dim1Code",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "Dim2Code",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "Dim3Code",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "Dim4Code",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "Dim5Code",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "LedgerAccountCode",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "Dim1Code",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "Dim2Code",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "Dim3Code",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "Dim4Code",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "Dim5Code",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "LedgerAccountCode",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "Dim1Code",
                table: "FINANCIAL_DOCUMENTS");

            migrationBuilder.DropColumn(
                name: "Dim2Code",
                table: "FINANCIAL_DOCUMENTS");

            migrationBuilder.DropColumn(
                name: "Dim3Code",
                table: "FINANCIAL_DOCUMENTS");

            migrationBuilder.DropColumn(
                name: "Dim4Code",
                table: "FINANCIAL_DOCUMENTS");

            migrationBuilder.DropColumn(
                name: "Dim5Code",
                table: "FINANCIAL_DOCUMENTS");

            migrationBuilder.DropColumn(
                name: "LedgerAccountCode",
                table: "FINANCIAL_DOCUMENTS");

            migrationBuilder.DropColumn(
                name: "Dim1Code",
                table: "FINANCIAL_ACCOUNT_ENTRIES");

            migrationBuilder.DropColumn(
                name: "Dim2Code",
                table: "FINANCIAL_ACCOUNT_ENTRIES");

            migrationBuilder.DropColumn(
                name: "Dim3Code",
                table: "FINANCIAL_ACCOUNT_ENTRIES");

            migrationBuilder.DropColumn(
                name: "Dim4Code",
                table: "FINANCIAL_ACCOUNT_ENTRIES");

            migrationBuilder.DropColumn(
                name: "Dim5Code",
                table: "FINANCIAL_ACCOUNT_ENTRIES");

            migrationBuilder.DropColumn(
                name: "LedgerAccountCode",
                table: "FINANCIAL_ACCOUNT_ENTRIES");

            migrationBuilder.DropColumn(
                name: "DimensionNumber",
                table: "COST_CENTERS");

            migrationBuilder.DropColumn(
                name: "DefaultDimensionCode",
                table: "BUSINESS_PARTNERS");

            migrationBuilder.RenameColumn(
                name: "Dim1Code",
                table: "SALES_INVOICES_ITEMS",
                newName: "CostCenterCode");
        }
    }
}
