using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTAbleStorageDailyBalanceDropColumnQualityLoss : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "QualityLossQty",
                table: "STORAGE_DAILY_BALANCES");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "QualityLossQty",
                table: "STORAGE_DAILY_BALANCES",
                type: "DECIMAL(18,3)",
                nullable: false,
                defaultValue: 0m);
        }
    }
}
