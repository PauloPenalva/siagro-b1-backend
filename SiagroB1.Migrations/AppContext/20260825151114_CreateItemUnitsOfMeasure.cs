using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class CreateItemUnitsOfMeasure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ITEM_UNITS_OF_MEASURE",
                columns: table => new
                {
                    ItemCode = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    Purpose = table.Column<int>(type: "int", nullable: false),
                    UnitOfMeasureCode = table.Column<string>(type: "VARCHAR(4)", nullable: false),
                    Factor = table.Column<decimal>(type: "DECIMAL(18,6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ITEM_UNITS_OF_MEASURE", x => new { x.ItemCode, x.Purpose });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ITEM_UNITS_OF_MEASURE");
        }
    }
}
