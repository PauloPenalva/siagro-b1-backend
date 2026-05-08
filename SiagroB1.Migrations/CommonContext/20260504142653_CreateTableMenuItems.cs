using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <inheritdoc />
    public partial class CreateTableMenuItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MENU_ITEMS",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    Key = table.Column<string>(type: "VARCHAR(50)", nullable: true),
                    Icon = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    Expanded = table.Column<bool>(type: "bit", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MENU_ITEMS", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MENU_ITEMS_MENU_ITEMS_ParentId",
                        column: x => x.ParentId,
                        principalTable: "MENU_ITEMS",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_MENU_ITEMS_ParentId",
                table: "MENU_ITEMS",
                column: "ParentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MENU_ITEMS");
        }
    }
}
