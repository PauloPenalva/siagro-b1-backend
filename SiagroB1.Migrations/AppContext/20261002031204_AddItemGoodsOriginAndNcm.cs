using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddItemGoodsOriginAndNcm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "GoodsOrigin",
                table: "ITEMS",
                type: "TINYINT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Ncm",
                table: "ITEMS",
                type: "VARCHAR(8)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GoodsOrigin",
                table: "ITEMS");

            migrationBuilder.DropColumn(
                name: "Ncm",
                table: "ITEMS");
        }
    }
}
