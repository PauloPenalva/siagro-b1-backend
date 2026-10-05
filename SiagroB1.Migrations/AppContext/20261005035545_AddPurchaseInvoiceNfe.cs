using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddPurchaseInvoiceNfe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CbsRate",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(7,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CbsRateReduction",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(7,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CbsValue",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Cfop",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "VARCHAR(4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CofinsBase",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CofinsRate",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(7,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CofinsValue",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "CreatesFinancialDocument",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CstCofins",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "VARCHAR(3)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CstIcms",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "VARCHAR(3)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CstPis",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "VARCHAR(3)",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "GoodsOrigin",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "TINYINT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "IbsCbsBase",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "IbsCbsClassCode",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "VARCHAR(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IbsCbsCst",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "VARCHAR(3)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "IbsMunicipalRate",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(7,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "IbsMunicipalValue",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "IbsRateReduction",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(7,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "IbsStateRate",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(7,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "IbsStateValue",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "IcmsBase",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "IcmsBaseReduction",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(7,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "IcmsBenefitCode",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "IcmsDeferral",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(7,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "IcmsDeferredValue",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "IcmsOperationValue",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "IcmsRate",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(7,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "IcmsValue",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "MovesFiscalInventory",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Ncm",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "VARCHAR(8)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NfeItemNumber",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PisBase",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PisRate",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(7,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PisValue",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "UsageCode",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UsageName",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "VARCHAR(200)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsNfeReturn",
                table: "PURCHASE_INVOICES",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "NfeAuthorizedAt",
                table: "PURCHASE_INVOICES",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NfeConfirmationError",
                table: "PURCHASE_INVOICES",
                type: "VARCHAR(500)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NfeEnvironment",
                table: "PURCHASE_INVOICES",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NfeProtocol",
                table: "PURCHASE_INVOICES",
                type: "VARCHAR(20)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NfeRandomCode",
                table: "PURCHASE_INVOICES",
                type: "VARCHAR(8)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NfeStatus",
                table: "PURCHASE_INVOICES",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "NfeStatusCode",
                table: "PURCHASE_INVOICES",
                type: "VARCHAR(4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NfeStatusReason",
                table: "PURCHASE_INVOICES",
                type: "VARCHAR(500)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentConditionCode",
                table: "PURCHASE_INVOICES",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferencedAccessKey",
                table: "PURCHASE_INVOICES",
                type: "VARCHAR(44)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PURCHASE_INVOICE_NFE_XMLS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseInvoiceKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Xml = table.Column<string>(type: "NVARCHAR(MAX)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PURCHASE_INVOICE_NFE_XMLS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_PURCHASE_INVOICE_NFE_XMLS_PURCHASE_INVOICES_PurchaseInvoiceKey",
                        column: x => x.PurchaseInvoiceKey,
                        principalTable: "PURCHASE_INVOICES",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_INVOICE_NFE_XMLS_PurchaseInvoiceKey_Kind",
                table: "PURCHASE_INVOICE_NFE_XMLS",
                columns: new[] { "PurchaseInvoiceKey", "Kind" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PURCHASE_INVOICE_NFE_XMLS");

            migrationBuilder.DropColumn(
                name: "CbsRate",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "CbsRateReduction",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "CbsValue",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "Cfop",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "CofinsBase",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "CofinsRate",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "CofinsValue",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "CreatesFinancialDocument",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "CstCofins",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "CstIcms",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "CstPis",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "GoodsOrigin",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "IbsCbsBase",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "IbsCbsClassCode",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "IbsCbsCst",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "IbsMunicipalRate",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "IbsMunicipalValue",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "IbsRateReduction",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "IbsStateRate",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "IbsStateValue",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "IcmsBase",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "IcmsBaseReduction",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "IcmsBenefitCode",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "IcmsDeferral",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "IcmsDeferredValue",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "IcmsOperationValue",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "IcmsRate",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "IcmsValue",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "MovesFiscalInventory",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "Ncm",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "NfeItemNumber",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "PisBase",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "PisRate",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "PisValue",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "UsageCode",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "UsageName",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "IsNfeReturn",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "NfeAuthorizedAt",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "NfeConfirmationError",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "NfeEnvironment",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "NfeProtocol",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "NfeRandomCode",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "NfeStatus",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "NfeStatusCode",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "NfeStatusReason",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "PaymentConditionCode",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "ReferencedAccessKey",
                table: "PURCHASE_INVOICES");
        }
    }
}
