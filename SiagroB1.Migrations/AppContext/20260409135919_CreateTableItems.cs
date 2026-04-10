using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class CreateTableItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ITEMS",
                columns: table => new
                {
                    ItemCode = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    ItemName = table.Column<string>(type: "VARCHAR(200)", nullable: false),
                    ItmsGrpCod = table.Column<short>(type: "smallint", nullable: true),
                    Enabled = table.Column<string>(type: "VARCHAR(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ITEMS", x => x.ItemCode);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ITEMS");
        }
    }
}
