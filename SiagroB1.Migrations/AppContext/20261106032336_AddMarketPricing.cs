using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddMarketPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MARKET_DESTINATIONS",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MARKET_DESTINATIONS", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MARKET_PRICE_TABLES",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Description = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    ItemCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    ItemName = table.Column<string>(type: "VARCHAR(200)", nullable: false),
                    HarvestSeasonCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MARKET_PRICE_TABLES", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MARKET_PRICE_TABLES_HARVEST_SEASSONS_HarvestSeasonCode",
                        column: x => x.HarvestSeasonCode,
                        principalTable: "HARVEST_SEASSONS",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MARKET_TRADE_TICKETS",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    PriceTableId = table.Column<int>(type: "int", nullable: false),
                    PriceTableLineId = table.Column<int>(type: "int", nullable: false),
                    PriceTableDescription = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    LineDescription = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    ItemCode = table.Column<string>(type: "VARCHAR(10)", nullable: true),
                    ItemName = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    HarvestSeasonCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    MunicipalityCode = table.Column<string>(type: "VARCHAR(7)", nullable: false),
                    DestinationId = table.Column<int>(type: "int", nullable: false),
                    DestinationName = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    InstrumentCode = table.Column<string>(type: "VARCHAR(30)", nullable: true),
                    ExchangeQuote = table.Column<decimal>(type: "DECIMAL(18,6)", nullable: false),
                    Premium = table.Column<decimal>(type: "DECIMAL(18,6)", nullable: false),
                    NetFx = table.Column<decimal>(type: "DECIMAL(18,6)", nullable: true),
                    SpotFx = table.Column<decimal>(type: "DECIMAL(18,6)", nullable: true),
                    FreightPerTon = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    PortPrice = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    CalculatedPrice = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    StepsJson = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    QuotedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    QuoteSource = table.Column<int>(type: "int", nullable: true),
                    DeliveryStartDate = table.Column<DateTime>(type: "DATE", nullable: true),
                    DeliveryEndDate = table.Column<DateTime>(type: "DATE", nullable: true),
                    ReceiptDate = table.Column<DateTime>(type: "DATE", nullable: true),
                    PaymentDate = table.Column<DateTime>(type: "DATE", nullable: true),
                    CardCode = table.Column<string>(type: "VARCHAR(10)", nullable: true),
                    CardName = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    VolumeKg = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    NegotiatedPrice = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    WarehouseCode = table.Column<string>(type: "VARCHAR(10)", nullable: true),
                    WarehouseName = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    AgentCode = table.Column<int>(type: "int", nullable: true),
                    AgentName = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    BranchCode = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    LogisticRegionCode = table.Column<string>(type: "VARCHAR(10)", nullable: true),
                    Notes = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    DeviationPercent = table.Column<decimal>(type: "DECIMAL(18,4)", nullable: false),
                    RequiresBoardApproval = table.Column<bool>(type: "bit", nullable: false),
                    BoardApprovalReason = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    ApprovedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BoardApprovedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    BoardApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    RejectedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectionReason = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    CanceledBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CanceledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PurchaseContractKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PurchaseContractCode = table.Column<string>(type: "VARCHAR(50)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MARKET_TRADE_TICKETS", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MARKET_TRADE_TICKETS_HARVEST_SEASSONS_HarvestSeasonCode",
                        column: x => x.HarvestSeasonCode,
                        principalTable: "HARVEST_SEASSONS",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MARKET_TRADE_TICKETS_MUNICIPALITIES_MunicipalityCode",
                        column: x => x.MunicipalityCode,
                        principalTable: "MUNICIPALITIES",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MARKET_TRADING_SETTINGS",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false),
                    SpotFxInstrumentCode = table.Column<string>(type: "VARCHAR(30)", nullable: true),
                    BoardApprovalVolumeTons = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: true),
                    BoardApprovalDeviationPercent = table.Column<decimal>(type: "DECIMAL(18,4)", nullable: true),
                    ContractUnitOfMeasureCode = table.Column<string>(type: "VARCHAR(4)", nullable: true),
                    ContractFreightUmCode = table.Column<string>(type: "VARCHAR(4)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MARKET_TRADING_SETTINGS", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "MARKET_FREIGHT_RATES",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MunicipalityCode = table.Column<string>(type: "VARCHAR(7)", nullable: false),
                    DestinationId = table.Column<int>(type: "int", nullable: false),
                    RatePerTon = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "DATE", nullable: false),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MARKET_FREIGHT_RATES", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MARKET_FREIGHT_RATES_MARKET_DESTINATIONS_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "MARKET_DESTINATIONS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MARKET_FREIGHT_RATES_MUNICIPALITIES_MunicipalityCode",
                        column: x => x.MunicipalityCode,
                        principalTable: "MUNICIPALITIES",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MARKET_PRICE_TABLE_LINES",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PriceTableId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    InstrumentCode = table.Column<string>(type: "VARCHAR(30)", nullable: false),
                    FxCurveInstrumentCode = table.Column<string>(type: "VARCHAR(30)", nullable: true),
                    DestinationId = table.Column<int>(type: "int", nullable: false),
                    Premium = table.Column<decimal>(type: "DECIMAL(18,6)", nullable: false),
                    ElevationCost = table.Column<decimal>(type: "DECIMAL(18,6)", nullable: false),
                    FxDiscount = table.Column<decimal>(type: "DECIMAL(18,6)", nullable: false),
                    DiscountValue = table.Column<decimal>(type: "DECIMAL(18,6)", nullable: false),
                    DiscountIsPercent = table.Column<bool>(type: "bit", nullable: false),
                    MarginPercent = table.Column<decimal>(type: "DECIMAL(18,6)", nullable: false),
                    DailyInterestPercent = table.Column<decimal>(type: "DECIMAL(18,6)", nullable: false),
                    TechnicalLossPercent = table.Column<decimal>(type: "DECIMAL(18,6)", nullable: false),
                    RuralTaxPercent = table.Column<decimal>(type: "DECIMAL(18,6)", nullable: false),
                    FreightCoefficientPercent = table.Column<decimal>(type: "DECIMAL(18,8)", nullable: false),
                    DeliveryStartDate = table.Column<DateTime>(type: "DATE", nullable: true),
                    DeliveryEndDate = table.Column<DateTime>(type: "DATE", nullable: true),
                    ReceiptDate = table.Column<DateTime>(type: "DATE", nullable: true),
                    PaymentDate = table.Column<DateTime>(type: "DATE", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MARKET_PRICE_TABLE_LINES", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MARKET_PRICE_TABLE_LINES_MARKET_DESTINATIONS_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "MARKET_DESTINATIONS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MARKET_PRICE_TABLE_LINES_MARKET_INSTRUMENTS_FxCurveInstrumentCode",
                        column: x => x.FxCurveInstrumentCode,
                        principalTable: "MARKET_INSTRUMENTS",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MARKET_PRICE_TABLE_LINES_MARKET_INSTRUMENTS_InstrumentCode",
                        column: x => x.InstrumentCode,
                        principalTable: "MARKET_INSTRUMENTS",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MARKET_PRICE_TABLE_LINES_MARKET_PRICE_TABLES_PriceTableId",
                        column: x => x.PriceTableId,
                        principalTable: "MARKET_PRICE_TABLES",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MARKET_DESTINATIONS_Name",
                table: "MARKET_DESTINATIONS",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MARKET_FREIGHT_RATES_DestinationId",
                table: "MARKET_FREIGHT_RATES",
                column: "DestinationId");

            migrationBuilder.CreateIndex(
                name: "IX_MARKET_FREIGHT_RATES_MunicipalityCode_DestinationId_ValidFrom",
                table: "MARKET_FREIGHT_RATES",
                columns: new[] { "MunicipalityCode", "DestinationId", "ValidFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MARKET_PRICE_TABLE_LINES_DestinationId",
                table: "MARKET_PRICE_TABLE_LINES",
                column: "DestinationId");

            migrationBuilder.CreateIndex(
                name: "IX_MARKET_PRICE_TABLE_LINES_FxCurveInstrumentCode",
                table: "MARKET_PRICE_TABLE_LINES",
                column: "FxCurveInstrumentCode");

            migrationBuilder.CreateIndex(
                name: "IX_MARKET_PRICE_TABLE_LINES_InstrumentCode",
                table: "MARKET_PRICE_TABLE_LINES",
                column: "InstrumentCode");

            migrationBuilder.CreateIndex(
                name: "IX_MARKET_PRICE_TABLE_LINES_PriceTableId",
                table: "MARKET_PRICE_TABLE_LINES",
                column: "PriceTableId");

            migrationBuilder.CreateIndex(
                name: "IX_MARKET_PRICE_TABLES_HarvestSeasonCode",
                table: "MARKET_PRICE_TABLES",
                column: "HarvestSeasonCode");

            migrationBuilder.CreateIndex(
                name: "IX_MARKET_TRADE_TICKETS_HarvestSeasonCode",
                table: "MARKET_TRADE_TICKETS",
                column: "HarvestSeasonCode");

            migrationBuilder.CreateIndex(
                name: "IX_MARKET_TRADE_TICKETS_MunicipalityCode",
                table: "MARKET_TRADE_TICKETS",
                column: "MunicipalityCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MARKET_FREIGHT_RATES");

            migrationBuilder.DropTable(
                name: "MARKET_PRICE_TABLE_LINES");

            migrationBuilder.DropTable(
                name: "MARKET_TRADE_TICKETS");

            migrationBuilder.DropTable(
                name: "MARKET_TRADING_SETTINGS");

            migrationBuilder.DropTable(
                name: "MARKET_DESTINATIONS");

            migrationBuilder.DropTable(
                name: "MARKET_PRICE_TABLES");
        }
    }
}
