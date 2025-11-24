using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AlterTableQualityAttribAddColumnType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Type",
                table: "QUALITY_INSPECTIONS");

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "QUALITY_ATTRIBS",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Type",
                table: "QUALITY_ATTRIBS");

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "QUALITY_INSPECTIONS",
                type: "int",
                nullable: true);
        }
    }
}
