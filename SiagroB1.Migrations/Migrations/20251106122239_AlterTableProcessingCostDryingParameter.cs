using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AlterTableProcessingCostDryingParameter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "Rate",
                table: "PROCESSING_COST_DRYING_PARAMETERS",
                type: "DECIMAL(18,8)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "DECIMAL(18,1)",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "Rate",
                table: "PROCESSING_COST_DRYING_PARAMETERS",
                type: "DECIMAL(18,1)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "DECIMAL(18,8)",
                oldNullable: true);
        }
    }
}
