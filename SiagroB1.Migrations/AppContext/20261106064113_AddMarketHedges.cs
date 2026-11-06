using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddMarketHedges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MARKET_COUNTERPARTIES",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    TaxId = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    Active = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MARKET_COUNTERPARTIES", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MARKET_COUNTERPARTY_ACCOUNTS",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CounterpartyId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "VARCHAR(30)", nullable: false),
                    Description = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    Active = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MARKET_COUNTERPARTY_ACCOUNTS", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MARKET_COUNTERPARTY_ACCOUNTS_MARKET_COUNTERPARTIES_CounterpartyId",
                        column: x => x.CounterpartyId,
                        principalTable: "MARKET_COUNTERPARTIES",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MARKET_BROKER_STATEMENTS",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "DATE", nullable: false),
                    DailyAdjustment = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    MarginRequired = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    MarginDeposited = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    Currency = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MARKET_BROKER_STATEMENTS", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MARKET_BROKER_STATEMENTS_MARKET_COUNTERPARTY_ACCOUNTS_AccountId",
                        column: x => x.AccountId,
                        principalTable: "MARKET_COUNTERPARTY_ACCOUNTS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MARKET_EFPS",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Date = table.Column<DateTime>(type: "DATE", nullable: false),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    InstrumentCode = table.Column<string>(type: "VARCHAR(30)", nullable: false),
                    Side = table.Column<int>(type: "int", nullable: false),
                    Lots = table.Column<int>(type: "int", nullable: false),
                    Price = table.Column<decimal>(type: "DECIMAL(18,6)", nullable: false),
                    Premium = table.Column<decimal>(type: "DECIMAL(18,6)", nullable: false),
                    FxRate = table.Column<decimal>(type: "DECIMAL(18,6)", nullable: true),
                    ContractSide = table.Column<int>(type: "int", nullable: false),
                    ContractKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractCode = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    VolumeKg = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    FixationPrice = table.Column<decimal>(type: "DECIMAL(18,8)", nullable: false),
                    FinancialDueDate = table.Column<DateTime>(type: "DATE", nullable: false),
                    FuturesTradeId = table.Column<int>(type: "int", nullable: true),
                    FixationKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CanceledBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CanceledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MARKET_EFPS", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MARKET_EFPS_MARKET_COUNTERPARTY_ACCOUNTS_AccountId",
                        column: x => x.AccountId,
                        principalTable: "MARKET_COUNTERPARTY_ACCOUNTS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MARKET_EFPS_MARKET_INSTRUMENTS_InstrumentCode",
                        column: x => x.InstrumentCode,
                        principalTable: "MARKET_INSTRUMENTS",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MARKET_FUTURES_TRADES",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TradedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TradeDate = table.Column<DateTime>(type: "DATE", nullable: false),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    InstrumentCode = table.Column<string>(type: "VARCHAR(30)", nullable: false),
                    Side = table.Column<int>(type: "int", nullable: false),
                    Lots = table.Column<int>(type: "int", nullable: false),
                    Price = table.Column<decimal>(type: "DECIMAL(18,6)", nullable: false),
                    Fees = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    FeesCurrency = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<int>(type: "int", nullable: false),
                    BrokerReference = table.Column<string>(type: "VARCHAR(50)", nullable: true),
                    OpenLots = table.Column<int>(type: "int", nullable: false),
                    Confirmed = table.Column<bool>(type: "bit", nullable: false),
                    ConfirmedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PurchaseContractKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SalesContractKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ContractCode = table.Column<string>(type: "VARCHAR(50)", nullable: true),
                    EfpId = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MARKET_FUTURES_TRADES", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MARKET_FUTURES_TRADES_MARKET_COUNTERPARTY_ACCOUNTS_AccountId",
                        column: x => x.AccountId,
                        principalTable: "MARKET_COUNTERPARTY_ACCOUNTS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MARKET_FUTURES_TRADES_MARKET_INSTRUMENTS_InstrumentCode",
                        column: x => x.InstrumentCode,
                        principalTable: "MARKET_INSTRUMENTS",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MARKET_FX_HEDGES",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TradedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TradeDate = table.Column<DateTime>(type: "DATE", nullable: false),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Side = table.Column<int>(type: "int", nullable: false),
                    AmountUsd = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    ContractedRate = table.Column<decimal>(type: "DECIMAL(18,6)", nullable: false),
                    SettlementDate = table.Column<DateTime>(type: "DATE", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SettlementRate = table.Column<decimal>(type: "DECIMAL(18,6)", nullable: true),
                    SettledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SettledBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    RealizedPnlBrl = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: true),
                    BankReference = table.Column<string>(type: "VARCHAR(50)", nullable: true),
                    Confirmed = table.Column<bool>(type: "bit", nullable: false),
                    ConfirmedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PurchaseContractKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SalesContractKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ContractCode = table.Column<string>(type: "VARCHAR(50)", nullable: true),
                    Notes = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MARKET_FX_HEDGES", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MARKET_FX_HEDGES_MARKET_COUNTERPARTY_ACCOUNTS_AccountId",
                        column: x => x.AccountId,
                        principalTable: "MARKET_COUNTERPARTY_ACCOUNTS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MARKET_FUTURES_MATCHES",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    InstrumentCode = table.Column<string>(type: "VARCHAR(30)", nullable: false),
                    OpenTradeId = table.Column<int>(type: "int", nullable: false),
                    CloseTradeId = table.Column<int>(type: "int", nullable: false),
                    Lots = table.Column<int>(type: "int", nullable: false),
                    OpenPrice = table.Column<decimal>(type: "DECIMAL(18,6)", nullable: false),
                    ClosePrice = table.Column<decimal>(type: "DECIMAL(18,6)", nullable: false),
                    RealizedPnl = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    Currency = table.Column<int>(type: "int", nullable: false),
                    CloseDate = table.Column<DateTime>(type: "DATE", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MARKET_FUTURES_MATCHES", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MARKET_FUTURES_MATCHES_MARKET_FUTURES_TRADES_CloseTradeId",
                        column: x => x.CloseTradeId,
                        principalTable: "MARKET_FUTURES_TRADES",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MARKET_FUTURES_MATCHES_MARKET_FUTURES_TRADES_OpenTradeId",
                        column: x => x.OpenTradeId,
                        principalTable: "MARKET_FUTURES_TRADES",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MARKET_BROKER_STATEMENTS_AccountId_Date",
                table: "MARKET_BROKER_STATEMENTS",
                columns: new[] { "AccountId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MARKET_COUNTERPARTIES_Name",
                table: "MARKET_COUNTERPARTIES",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MARKET_COUNTERPARTY_ACCOUNTS_CounterpartyId_Code",
                table: "MARKET_COUNTERPARTY_ACCOUNTS",
                columns: new[] { "CounterpartyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MARKET_EFPS_AccountId",
                table: "MARKET_EFPS",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MARKET_EFPS_InstrumentCode",
                table: "MARKET_EFPS",
                column: "InstrumentCode");

            migrationBuilder.CreateIndex(
                name: "IX_MARKET_FUTURES_MATCHES_CloseTradeId",
                table: "MARKET_FUTURES_MATCHES",
                column: "CloseTradeId");

            migrationBuilder.CreateIndex(
                name: "IX_MARKET_FUTURES_MATCHES_OpenTradeId",
                table: "MARKET_FUTURES_MATCHES",
                column: "OpenTradeId");

            migrationBuilder.CreateIndex(
                name: "IX_MARKET_FUTURES_TRADES_AccountId_InstrumentCode_TradedAt",
                table: "MARKET_FUTURES_TRADES",
                columns: new[] { "AccountId", "InstrumentCode", "TradedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MARKET_FUTURES_TRADES_InstrumentCode",
                table: "MARKET_FUTURES_TRADES",
                column: "InstrumentCode");

            migrationBuilder.CreateIndex(
                name: "IX_MARKET_FX_HEDGES_AccountId",
                table: "MARKET_FX_HEDGES",
                column: "AccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MARKET_BROKER_STATEMENTS");

            migrationBuilder.DropTable(
                name: "MARKET_EFPS");

            migrationBuilder.DropTable(
                name: "MARKET_FUTURES_MATCHES");

            migrationBuilder.DropTable(
                name: "MARKET_FX_HEDGES");

            migrationBuilder.DropTable(
                name: "MARKET_FUTURES_TRADES");

            migrationBuilder.DropTable(
                name: "MARKET_COUNTERPARTY_ACCOUNTS");

            migrationBuilder.DropTable(
                name: "MARKET_COUNTERPARTIES");
        }
    }
}
