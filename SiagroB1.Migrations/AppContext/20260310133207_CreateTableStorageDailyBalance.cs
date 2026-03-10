using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class CreateTableStorageDailyBalance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "STORAGE_CHARGES",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StorageAddressCode = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    ChargeType = table.Column<int>(type: "int", nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BaseQuantity = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    TonDays = table.Column<decimal>(type: "DECIMAL(18,6)", nullable: false),
                    UnitPriceOrRate = table.Column<decimal>(type: "DECIMAL(18,8)", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    TotalQuantityLoss = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    ProcessingCostCode = table.Column<string>(type: "VARCHAR(10)", nullable: true),
                    CalculationDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    IsInvoiced = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_STORAGE_CHARGES", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "STORAGE_DAILY_BALANCES",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StorageAddressCode = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    BalanceDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OpeningBalance = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    ReceiptQty = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    ShipmentQty = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    QualityLossQty = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    TechnicalLossQty = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    ClosingBalance = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    BillableBalance = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_STORAGE_DAILY_BALANCES", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_CHARGES_StorageAddressCode_ChargeType_PeriodStart_PeriodEnd",
                table: "STORAGE_CHARGES",
                columns: new[] { "StorageAddressCode", "ChargeType", "PeriodStart", "PeriodEnd" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_DAILY_BALANCES_StorageAddressCode_BalanceDate",
                table: "STORAGE_DAILY_BALANCES",
                columns: new[] { "StorageAddressCode", "BalanceDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "STORAGE_CHARGES");

            migrationBuilder.DropTable(
                name: "STORAGE_DAILY_BALANCES");
        }
    }
}
