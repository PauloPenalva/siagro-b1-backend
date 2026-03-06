using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTableProcessingCostAddColumnDryingCalculationMethod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DryingCalculationMethod",
                table: "PROCESSING_COSTS",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "StoragePriceCalculationMethod",
                table: "PROCESSING_COSTS",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DryingCalculationMethod",
                table: "PROCESSING_COSTS");

            migrationBuilder.DropColumn(
                name: "StoragePriceCalculationMethod",
                table: "PROCESSING_COSTS");
        }
    }
}
