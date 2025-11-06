using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AlterTableProcessingCosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "FumigationGraceDays",
                table: "PROCESSING_COSTS",
                newName: "FumigationIntervalDays");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "FumigationIntervalDays",
                table: "PROCESSING_COSTS",
                newName: "FumigationGraceDays");
        }
    }
}
