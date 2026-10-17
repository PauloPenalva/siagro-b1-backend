using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddDifalAndTaxEffect : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PurchasePurpose",
                table: "USAGE_EFFECTS",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "DifalBase",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DifalInternalRate",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(7,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DifalInterstateRate",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(7,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DifalValue",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FcpRate",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(7,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FcpValue",
                table: "PURCHASE_INVOICES_ITEMS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "PisCofinsRegime",
                table: "BRANCHS",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ICMS_ITEM_STATE_RATES",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemCode = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    StateCode = table.Column<string>(type: "VARCHAR(2)", nullable: false),
                    InternalRate = table.Column<decimal>(type: "DECIMAL(7,4)", nullable: false),
                    FcpRate = table.Column<decimal>(type: "DECIMAL(7,4)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ICMS_ITEM_STATE_RATES", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "ICMS_STATE_SETTINGS",
                columns: table => new
                {
                    StateCode = table.Column<string>(type: "VARCHAR(2)", nullable: false),
                    InternalRate = table.Column<decimal>(type: "DECIMAL(7,4)", nullable: false),
                    FcpRate = table.Column<decimal>(type: "DECIMAL(7,4)", nullable: false),
                    DifalBaseRule = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ICMS_STATE_SETTINGS", x => x.StateCode);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ICMS_ITEM_STATE_RATES_ItemCode_StateCode",
                table: "ICMS_ITEM_STATE_RATES",
                columns: new[] { "ItemCode", "StateCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ICMS_ITEM_STATE_RATES");

            migrationBuilder.DropTable(
                name: "ICMS_STATE_SETTINGS");

            migrationBuilder.DropColumn(
                name: "PurchasePurpose",
                table: "USAGE_EFFECTS");

            migrationBuilder.DropColumn(
                name: "DifalBase",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "DifalInternalRate",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "DifalInterstateRate",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "DifalValue",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "FcpRate",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "FcpValue",
                table: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "PisCofinsRegime",
                table: "BRANCHS");
        }
    }
}
