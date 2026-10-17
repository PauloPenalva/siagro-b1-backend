using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddServiceWithholdings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "WithholdingCofinsRate",
                table: "USAGES",
                type: "DECIMAL(7,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WithholdingCsllRate",
                table: "USAGES",
                type: "DECIMAL(7,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WithholdingInssRate",
                table: "USAGES",
                type: "DECIMAL(7,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WithholdingIrrfRate",
                table: "USAGES",
                type: "DECIMAL(7,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WithholdingIssRate",
                table: "USAGES",
                type: "DECIMAL(7,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WithholdingPisRate",
                table: "USAGES",
                type: "DECIMAL(7,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WithheldCofins",
                table: "PURCHASE_INVOICES",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "WithheldCsll",
                table: "PURCHASE_INVOICES",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "WithheldInss",
                table: "PURCHASE_INVOICES",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "WithheldIrrf",
                table: "PURCHASE_INVOICES",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "WithheldIss",
                table: "PURCHASE_INVOICES",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "WithheldPis",
                table: "PURCHASE_INVOICES",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "WithholdingBase",
                table: "PURCHASE_INVOICES",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "WithholdingsManual",
                table: "PURCHASE_INVOICES",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "WITHHOLDING_TAX_SETTINGS",
                columns: table => new
                {
                    TaxType = table.Column<int>(type: "int", nullable: false),
                    CardCode = table.Column<string>(type: "VARCHAR(15)", nullable: false),
                    CardName = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    DueDay = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WITHHOLDING_TAX_SETTINGS", x => x.TaxType);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WITHHOLDING_TAX_SETTINGS");

            migrationBuilder.DropColumn(
                name: "WithholdingCofinsRate",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "WithholdingCsllRate",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "WithholdingInssRate",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "WithholdingIrrfRate",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "WithholdingIssRate",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "WithholdingPisRate",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "WithheldCofins",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "WithheldCsll",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "WithheldInss",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "WithheldIrrf",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "WithheldIss",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "WithheldPis",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "WithholdingBase",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "WithholdingsManual",
                table: "PURCHASE_INVOICES");
        }
    }
}
