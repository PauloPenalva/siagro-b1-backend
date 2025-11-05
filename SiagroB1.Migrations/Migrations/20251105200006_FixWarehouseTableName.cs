using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class FixWarehouseTableName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_LOTS_WHAREHOUSE_WhareHouseKey",
                table: "STORAGE_LOTS");

            migrationBuilder.DropTable(
                name: "WHAREHOUSE");

            migrationBuilder.CreateTable(
                name: "WHAREHOUSES",
                columns: table => new
                {
                    BranchKey = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    Key = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    Name = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    TAXID = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    Inactive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WHAREHOUSES", x => x.Key);
                    table.ForeignKey(
                        name: "FK_WHAREHOUSES_BRANCHS_BranchKey",
                        column: x => x.BranchKey,
                        principalTable: "BRANCHS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_WHAREHOUSES_BranchKey",
                table: "WHAREHOUSES",
                column: "BranchKey");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_LOTS_WHAREHOUSES_WhareHouseKey",
                table: "STORAGE_LOTS",
                column: "WhareHouseKey",
                principalTable: "WHAREHOUSES",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_LOTS_WHAREHOUSES_WhareHouseKey",
                table: "STORAGE_LOTS");

            migrationBuilder.DropTable(
                name: "WHAREHOUSES");

            migrationBuilder.CreateTable(
                name: "WHAREHOUSE",
                columns: table => new
                {
                    BranchKey = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    Key = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    Inactive = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    TAXID = table.Column<string>(type: "VARCHAR(14)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WHAREHOUSE", x => x.Key);
                    table.ForeignKey(
                        name: "FK_WHAREHOUSE_BRANCHS_BranchKey",
                        column: x => x.BranchKey,
                        principalTable: "BRANCHS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_WHAREHOUSE_BranchKey",
                table: "WHAREHOUSE",
                column: "BranchKey");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_LOTS_WHAREHOUSE_WhareHouseKey",
                table: "STORAGE_LOTS",
                column: "WhareHouseKey",
                principalTable: "WHAREHOUSE",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
