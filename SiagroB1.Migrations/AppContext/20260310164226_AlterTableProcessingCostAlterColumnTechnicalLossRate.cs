using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTableProcessingCostAlterColumnTechnicalLossRate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "TechnicalLossRate",
                table: "PROCESSING_COSTS",
                type: "DECIMAL(18,12)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "DECIMAL(18,8)",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "TechnicalLossRate",
                table: "PROCESSING_COSTS",
                type: "DECIMAL(18,8)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "DECIMAL(18,12)",
                oldNullable: true);
        }
    }
}
