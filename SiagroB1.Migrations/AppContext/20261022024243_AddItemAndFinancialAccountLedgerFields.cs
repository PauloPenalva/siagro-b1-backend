using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddItemAndFinancialAccountLedgerFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccountingItemCode",
                table: "ITEMS",
                type: "VARCHAR(20)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CostCenterCode",
                table: "ITEMS",
                type: "VARCHAR(20)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LedgerAccountCode",
                table: "ITEMS",
                type: "VARCHAR(20)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LedgerAccountCode",
                table: "FINANCIAL_ACCOUNTS",
                type: "VARCHAR(20)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccountingItemCode",
                table: "ITEMS");

            migrationBuilder.DropColumn(
                name: "CostCenterCode",
                table: "ITEMS");

            migrationBuilder.DropColumn(
                name: "LedgerAccountCode",
                table: "ITEMS");

            migrationBuilder.DropColumn(
                name: "LedgerAccountCode",
                table: "FINANCIAL_ACCOUNTS");
        }
    }
}
