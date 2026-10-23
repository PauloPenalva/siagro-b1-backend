using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddUsagePisCofinsEfdCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PisCofinsCreditBaseCode",
                table: "USAGES",
                type: "VARCHAR(2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PisCofinsRevenueNatureCode",
                table: "USAGES",
                type: "VARCHAR(3)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PisCofinsCreditBaseCode",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "PisCofinsRevenueNatureCode",
                table: "USAGES");
        }
    }
}
