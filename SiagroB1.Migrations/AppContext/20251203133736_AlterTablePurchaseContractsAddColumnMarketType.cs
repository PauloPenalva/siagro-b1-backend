#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTablePurchaseContractsAddColumnMarketType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MarketType",
                table: "PURCHASE_CONTRACTS",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StandardCashFlowDate",
                table: "PURCHASE_CONTRACTS",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MarketType",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "StandardCashFlowDate",
                table: "PURCHASE_CONTRACTS");
        }
    }
}
