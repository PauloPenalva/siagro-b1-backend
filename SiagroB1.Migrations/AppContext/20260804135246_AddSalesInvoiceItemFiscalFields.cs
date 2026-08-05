using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddSalesInvoiceItemFiscalFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Cfop",
                table: "SALES_INVOICES_ITEMS",
                type: "VARCHAR(4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CofinsBase",
                table: "SALES_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CofinsRate",
                table: "SALES_INVOICES_ITEMS",
                type: "DECIMAL(5,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CofinsValue",
                table: "SALES_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "CostCenterCode",
                table: "SALES_INVOICES_ITEMS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CstCofins",
                table: "SALES_INVOICES_ITEMS",
                type: "VARCHAR(3)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CstIcms",
                table: "SALES_INVOICES_ITEMS",
                type: "VARCHAR(3)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CstPis",
                table: "SALES_INVOICES_ITEMS",
                type: "VARCHAR(3)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "IcmsBase",
                table: "SALES_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "IcmsRate",
                table: "SALES_INVOICES_ITEMS",
                type: "DECIMAL(5,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "IcmsValue",
                table: "SALES_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "LedgerAccountCode",
                table: "SALES_INVOICES_ITEMS",
                type: "VARCHAR(20)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Ncm",
                table: "SALES_INVOICES_ITEMS",
                type: "VARCHAR(8)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PisBase",
                table: "SALES_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PisRate",
                table: "SALES_INVOICES_ITEMS",
                type: "DECIMAL(5,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PisValue",
                table: "SALES_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Cfop",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "CofinsBase",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "CofinsRate",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "CofinsValue",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "CostCenterCode",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "CstCofins",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "CstIcms",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "CstPis",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "IcmsBase",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "IcmsRate",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "IcmsValue",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "LedgerAccountCode",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "Ncm",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "PisBase",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "PisRate",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "PisValue",
                table: "SALES_INVOICES_ITEMS");
        }
    }
}
