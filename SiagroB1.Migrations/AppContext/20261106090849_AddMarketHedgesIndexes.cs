using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddMarketHedgesIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_MARKET_FUTURES_TRADES_EfpId",
                table: "MARKET_FUTURES_TRADES",
                column: "EfpId",
                unique: true,
                filter: "[EfpId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MARKET_FUTURES_MATCHES_AccountId_InstrumentCode",
                table: "MARKET_FUTURES_MATCHES",
                columns: new[] { "AccountId", "InstrumentCode" });

            migrationBuilder.CreateIndex(
                name: "IX_MARKET_EFPS_ContractKey",
                table: "MARKET_EFPS",
                column: "ContractKey");

            migrationBuilder.CreateIndex(
                name: "IX_MARKET_EFPS_FixationKey",
                table: "MARKET_EFPS",
                column: "FixationKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MARKET_FUTURES_TRADES_EfpId",
                table: "MARKET_FUTURES_TRADES");

            migrationBuilder.DropIndex(
                name: "IX_MARKET_FUTURES_MATCHES_AccountId_InstrumentCode",
                table: "MARKET_FUTURES_MATCHES");

            migrationBuilder.DropIndex(
                name: "IX_MARKET_EFPS_ContractKey",
                table: "MARKET_EFPS");

            migrationBuilder.DropIndex(
                name: "IX_MARKET_EFPS_FixationKey",
                table: "MARKET_EFPS");
        }
    }
}
