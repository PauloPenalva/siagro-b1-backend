using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class CreateWarehouseComplements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WAREHOUSE_COMPLEMENTS",
                columns: table => new
                {
                    WarehouseCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    IsParticipant = table.Column<bool>(type: "BIT", nullable: false),
                    IsOwn = table.Column<bool>(type: "BIT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WAREHOUSE_COMPLEMENTS", x => x.WarehouseCode);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WAREHOUSE_COMPLEMENTS");
        }
    }
}
