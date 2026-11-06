using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddMarketQuotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CMA_SETTINGS",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    Host = table.Column<string>(type: "VARCHAR(200)", nullable: false),
                    UserName = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    PasswordEncrypted = table.Column<byte[]>(type: "VARBINARY(512)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CMA_SETTINGS", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "MARKET_INSTRUMENTS",
                columns: table => new
                {
                    Code = table.Column<string>(type: "VARCHAR(30)", nullable: false),
                    CmaSourceId = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    Exchange = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Commodity = table.Column<string>(type: "VARCHAR(20)", nullable: false),
                    ItemCode = table.Column<string>(type: "VARCHAR(50)", nullable: true),
                    ExpirationDate = table.Column<DateTime>(type: "DATE", nullable: true),
                    QuoteUnit = table.Column<int>(type: "int", nullable: false),
                    ContractSize = table.Column<decimal>(type: "DECIMAL(18,6)", nullable: false),
                    TonFactor = table.Column<decimal>(type: "DECIMAL(18,6)", nullable: false),
                    CurveDays = table.Column<int>(type: "int", nullable: true),
                    GroupName = table.Column<string>(type: "VARCHAR(40)", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MARKET_INSTRUMENTS", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "MARKET_DAILY_CLOSES",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InstrumentCode = table.Column<string>(type: "VARCHAR(30)", nullable: false),
                    Date = table.Column<DateTime>(type: "DATE", nullable: false),
                    Close = table.Column<decimal>(type: "DECIMAL(18,6)", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    IsSettlement = table.Column<bool>(type: "bit", nullable: false),
                    UserName = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MARKET_DAILY_CLOSES", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MARKET_DAILY_CLOSES_MARKET_INSTRUMENTS_InstrumentCode",
                        column: x => x.InstrumentCode,
                        principalTable: "MARKET_INSTRUMENTS",
                        principalColumn: "Code");
                });

            migrationBuilder.CreateTable(
                name: "MARKET_QUOTES",
                columns: table => new
                {
                    InstrumentCode = table.Column<string>(type: "VARCHAR(30)", nullable: false),
                    Last = table.Column<decimal>(type: "DECIMAL(18,6)", nullable: true),
                    Bid = table.Column<decimal>(type: "DECIMAL(18,6)", nullable: true),
                    Ask = table.Column<decimal>(type: "DECIMAL(18,6)", nullable: true),
                    ChangePercent = table.Column<decimal>(type: "DECIMAL(18,6)", nullable: true),
                    High = table.Column<decimal>(type: "DECIMAL(18,6)", nullable: true),
                    Low = table.Column<decimal>(type: "DECIMAL(18,6)", nullable: true),
                    Open = table.Column<decimal>(type: "DECIMAL(18,6)", nullable: true),
                    PreviousClose = table.Column<decimal>(type: "DECIMAL(18,6)", nullable: true),
                    IsSettlement = table.Column<bool>(type: "bit", nullable: false),
                    QuotedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    UserName = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    ManualOverride = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MARKET_QUOTES", x => x.InstrumentCode);
                    table.ForeignKey(
                        name: "FK_MARKET_QUOTES_MARKET_INSTRUMENTS_InstrumentCode",
                        column: x => x.InstrumentCode,
                        principalTable: "MARKET_INSTRUMENTS",
                        principalColumn: "Code");
                });

            migrationBuilder.CreateIndex(
                name: "IX_MARKET_DAILY_CLOSES_InstrumentCode_Date",
                table: "MARKET_DAILY_CLOSES",
                columns: new[] { "InstrumentCode", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CMA_SETTINGS");

            migrationBuilder.DropTable(
                name: "MARKET_DAILY_CLOSES");

            migrationBuilder.DropTable(
                name: "MARKET_QUOTES");

            migrationBuilder.DropTable(
                name: "MARKET_INSTRUMENTS");
        }
    }
}
