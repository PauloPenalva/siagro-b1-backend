using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddSalesInvoiceItemTaxSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            WidenRate(migrationBuilder, "PisRate");
            WidenRate(migrationBuilder, "IcmsRate");
            WidenRate(migrationBuilder, "CofinsRate");
            migrationBuilder.AddColumn<decimal>(
                name: "CbsRate",
                table: "SALES_INVOICES_ITEMS",
                type: "DECIMAL(7,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CbsRateReduction",
                table: "SALES_INVOICES_ITEMS",
                type: "DECIMAL(7,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CbsValue",
                table: "SALES_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "CreatesFinancialDocument",
                table: "SALES_INVOICES_ITEMS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte>(
                name: "GoodsOrigin",
                table: "SALES_INVOICES_ITEMS",
                type: "TINYINT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "IbsCbsBase",
                table: "SALES_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "IbsCbsClassCode",
                table: "SALES_INVOICES_ITEMS",
                type: "VARCHAR(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IbsCbsCst",
                table: "SALES_INVOICES_ITEMS",
                type: "VARCHAR(3)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "IbsMunicipalRate",
                table: "SALES_INVOICES_ITEMS",
                type: "DECIMAL(7,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "IbsMunicipalValue",
                table: "SALES_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "IbsRateReduction",
                table: "SALES_INVOICES_ITEMS",
                type: "DECIMAL(7,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "IbsStateRate",
                table: "SALES_INVOICES_ITEMS",
                type: "DECIMAL(7,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "IbsStateValue",
                table: "SALES_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "IcmsBaseReduction",
                table: "SALES_INVOICES_ITEMS",
                type: "DECIMAL(7,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "IcmsBenefitCode",
                table: "SALES_INVOICES_ITEMS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "IcmsDeferral",
                table: "SALES_INVOICES_ITEMS",
                type: "DECIMAL(7,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "IcmsDeferredValue",
                table: "SALES_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "IcmsOperationValue",
                table: "SALES_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "MovesFiscalInventory",
                table: "SALES_INVOICES_ITEMS",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CbsRate",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "CbsRateReduction",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "CbsValue",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "CreatesFinancialDocument",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "GoodsOrigin",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "IbsCbsBase",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "IbsCbsClassCode",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "IbsCbsCst",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "IbsMunicipalRate",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "IbsMunicipalValue",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "IbsRateReduction",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "IbsStateRate",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "IbsStateValue",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "IcmsBaseReduction",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "IcmsBenefitCode",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "IcmsDeferral",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "IcmsDeferredValue",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "IcmsOperationValue",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "MovesFiscalInventory",
                table: "SALES_INVOICES_ITEMS");

            NarrowRate(migrationBuilder, "PisRate");
            NarrowRate(migrationBuilder, "IcmsRate");
            NarrowRate(migrationBuilder, "CofinsRate");
        }

        /// <summary>
        /// As alíquotas passam a guardar percentual (18% = 18,0000), que não cabe em DECIMAL(5,4).
        /// Escrito à mão: o AlterColumn do EF derruba o DEFAULT 0 da coluna e não o recria. O
        /// default sai, o tipo muda e o default volta. Só alarga: nenhum valor gravado muda.
        /// </summary>
        private static void WidenRate(MigrationBuilder migrationBuilder, string column) =>
            ChangeRatePrecision(migrationBuilder, column, "DECIMAL(7,4)");

        private static void NarrowRate(MigrationBuilder migrationBuilder, string column) =>
            ChangeRatePrecision(migrationBuilder, column, "DECIMAL(5,4)");

        private static void ChangeRatePrecision(MigrationBuilder migrationBuilder, string column, string type) =>
            migrationBuilder.Sql($@"
DECLARE @df sysname;
SELECT @df = dc.name FROM sys.default_constraints dc
JOIN sys.columns c ON c.default_object_id = dc.object_id
WHERE dc.parent_object_id = OBJECT_ID('SALES_INVOICES_ITEMS') AND c.name = '{column}';
IF @df IS NOT NULL EXEC('ALTER TABLE SALES_INVOICES_ITEMS DROP CONSTRAINT [' + @df + ']');
ALTER TABLE SALES_INVOICES_ITEMS ALTER COLUMN [{column}] {type} NOT NULL;
ALTER TABLE SALES_INVOICES_ITEMS ADD DEFAULT 0 FOR [{column}];");
    }
}
