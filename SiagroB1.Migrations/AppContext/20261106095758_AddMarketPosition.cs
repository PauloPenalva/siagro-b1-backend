using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddMarketPosition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MarketExchangePrice",
                table: "SALES_CONTRACTS",
                type: "DECIMAL(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MarketFxLocked",
                table: "SALES_CONTRACTS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "MarketFxRate",
                table: "SALES_CONTRACTS",
                type: "DECIMAL(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MarketInstrumentCode",
                table: "SALES_CONTRACTS",
                type: "VARCHAR(30)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MarketPremium",
                table: "SALES_CONTRACTS",
                type: "DECIMAL(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MarketUsdPrice",
                table: "SALES_CONTRACTS",
                type: "DECIMAL(18,8)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MarketExchangePrice",
                table: "PURCHASE_CONTRACTS",
                type: "DECIMAL(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MarketFxLocked",
                table: "PURCHASE_CONTRACTS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "MarketFxRate",
                table: "PURCHASE_CONTRACTS",
                type: "DECIMAL(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MarketInstrumentCode",
                table: "PURCHASE_CONTRACTS",
                type: "VARCHAR(30)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MarketPremium",
                table: "PURCHASE_CONTRACTS",
                type: "DECIMAL(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MarketUsdPrice",
                table: "PURCHASE_CONTRACTS",
                type: "DECIMAL(18,8)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MARKET_COMMODITIES",
                columns: table => new
                {
                    Code = table.Column<string>(type: "VARCHAR(20)", nullable: false),
                    Name = table.Column<string>(type: "VARCHAR(60)", nullable: false),
                    HedgeExchange = table.Column<int>(type: "int", nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MARKET_COMMODITIES", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "MARKET_COMMODITY_ITEMS",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CommodityCode = table.Column<string>(type: "VARCHAR(20)", nullable: false),
                    ItemCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    ItemName = table.Column<string>(type: "VARCHAR(200)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MARKET_COMMODITY_ITEMS", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MARKET_COMMODITY_ITEMS_MARKET_COMMODITIES_CommodityCode",
                        column: x => x.CommodityCode,
                        principalTable: "MARKET_COMMODITIES",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MARKET_COMMODITY_MONTHS",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CommodityCode = table.Column<string>(type: "VARCHAR(20)", nullable: false),
                    DeliveryMonth = table.Column<int>(type: "int", nullable: false),
                    FuturesMonth = table.Column<int>(type: "int", nullable: false),
                    YearOffset = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MARKET_COMMODITY_MONTHS", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MARKET_COMMODITY_MONTHS_MARKET_COMMODITIES_CommodityCode",
                        column: x => x.CommodityCode,
                        principalTable: "MARKET_COMMODITIES",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MARKET_EXPOSURE_LIMITS",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Scope = table.Column<int>(type: "int", nullable: false),
                    CommodityCode = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    Year = table.Column<int>(type: "int", nullable: true),
                    Month = table.Column<int>(type: "int", nullable: true),
                    MaxLong = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: true),
                    MaxShort = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: true),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MARKET_EXPOSURE_LIMITS", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MARKET_EXPOSURE_LIMITS_MARKET_COMMODITIES_CommodityCode",
                        column: x => x.CommodityCode,
                        principalTable: "MARKET_COMMODITIES",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MARKET_LIMIT_BREACHES",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LimitId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "DATE", nullable: false),
                    Value = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    LimitValue = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NotifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MARKET_LIMIT_BREACHES", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MARKET_LIMIT_BREACHES_MARKET_EXPOSURE_LIMITS_LimitId",
                        column: x => x.LimitId,
                        principalTable: "MARKET_EXPOSURE_LIMITS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MARKET_COMMODITY_ITEMS_CommodityCode",
                table: "MARKET_COMMODITY_ITEMS",
                column: "CommodityCode");

            migrationBuilder.CreateIndex(
                name: "IX_MARKET_COMMODITY_ITEMS_ItemCode",
                table: "MARKET_COMMODITY_ITEMS",
                column: "ItemCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MARKET_COMMODITY_MONTHS_CommodityCode_DeliveryMonth",
                table: "MARKET_COMMODITY_MONTHS",
                columns: new[] { "CommodityCode", "DeliveryMonth" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MARKET_EXPOSURE_LIMITS_CommodityCode",
                table: "MARKET_EXPOSURE_LIMITS",
                column: "CommodityCode");

            migrationBuilder.CreateIndex(
                name: "IX_MARKET_LIMIT_BREACHES_LimitId_Date",
                table: "MARKET_LIMIT_BREACHES",
                columns: new[] { "LimitId", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MARKET_COMMODITY_ITEMS");

            migrationBuilder.DropTable(
                name: "MARKET_COMMODITY_MONTHS");

            migrationBuilder.DropTable(
                name: "MARKET_LIMIT_BREACHES");

            migrationBuilder.DropTable(
                name: "MARKET_EXPOSURE_LIMITS");

            migrationBuilder.DropTable(
                name: "MARKET_COMMODITIES");

            migrationBuilder.DropColumn(
                name: "MarketExchangePrice",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "MarketFxLocked",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "MarketFxRate",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "MarketInstrumentCode",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "MarketPremium",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "MarketUsdPrice",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "MarketExchangePrice",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "MarketFxLocked",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "MarketFxRate",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "MarketInstrumentCode",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "MarketPremium",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "MarketUsdPrice",
                table: "PURCHASE_CONTRACTS");
        }
    }
}
