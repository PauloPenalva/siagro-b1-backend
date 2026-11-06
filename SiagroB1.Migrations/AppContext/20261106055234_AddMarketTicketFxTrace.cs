using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddMarketTicketFxTrace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CurveFx",
                table: "MARKET_TRADE_TICKETS",
                type: "DECIMAL(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FxQuoteSource",
                table: "MARKET_TRADE_TICKETS",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FxQuotedAt",
                table: "MARKET_TRADE_TICKETS",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurveFx",
                table: "MARKET_TRADE_TICKETS");

            migrationBuilder.DropColumn(
                name: "FxQuoteSource",
                table: "MARKET_TRADE_TICKETS");

            migrationBuilder.DropColumn(
                name: "FxQuotedAt",
                table: "MARKET_TRADE_TICKETS");
        }
    }
}
