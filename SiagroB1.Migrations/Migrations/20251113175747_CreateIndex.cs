using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class CreateIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TRUCKS_BranchKey",
                table: "TRUCKS");

            migrationBuilder.DropIndex(
                name: "IX_TRUCK_DRIVERS_BranchKey",
                table: "TRUCK_DRIVERS");

            migrationBuilder.AlterColumn<string>(
                name: "VARCHAR(7) NOT NULL",
                table: "TRUCKS",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_TRUCKS_BranchKey_VARCHAR(7) NOT NULL",
                table: "TRUCKS",
                columns: new[] { "BranchKey", "VARCHAR(7) NOT NULL" },
                unique: true,
                filter: "[BranchKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TRUCK_DRIVERS_BranchKey_Cpf",
                table: "TRUCK_DRIVERS",
                columns: new[] { "BranchKey", "Cpf" },
                unique: true,
                filter: "[BranchKey] IS NOT NULL AND [Cpf] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TRUCKS_BranchKey_VARCHAR(7) NOT NULL",
                table: "TRUCKS");

            migrationBuilder.DropIndex(
                name: "IX_TRUCK_DRIVERS_BranchKey_Cpf",
                table: "TRUCK_DRIVERS");

            migrationBuilder.AlterColumn<string>(
                name: "VARCHAR(7) NOT NULL",
                table: "TRUCKS",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.CreateIndex(
                name: "IX_TRUCKS_BranchKey",
                table: "TRUCKS",
                column: "BranchKey");

            migrationBuilder.CreateIndex(
                name: "IX_TRUCK_DRIVERS_BranchKey",
                table: "TRUCK_DRIVERS",
                column: "BranchKey");
        }
    }
}
