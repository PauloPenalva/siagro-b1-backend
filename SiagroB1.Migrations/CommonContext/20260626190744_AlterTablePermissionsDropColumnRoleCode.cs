using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <inheritdoc />
    public partial class AlterTablePermissionsDropColumnRoleCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "ROLES",
                type: "VARCHAR(100)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RoleCode",
                table: "PERMISSIONS",
                type: "VARCHAR(50)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ROLE_MENUS",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleCode = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    MenuItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ROLE_MENUS", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ROLE_MENUS_MENU_ITEMS_MenuItemId",
                        column: x => x.MenuItemId,
                        principalTable: "MENU_ITEMS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ROLE_MENUS_ROLES_RoleCode",
                        column: x => x.RoleCode,
                        principalTable: "ROLES",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PERMISSIONS_RoleCode",
                table: "PERMISSIONS",
                column: "RoleCode");

            migrationBuilder.CreateIndex(
                name: "IX_ROLE_MENUS_MenuItemId",
                table: "ROLE_MENUS",
                column: "MenuItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ROLE_MENUS_RoleCode",
                table: "ROLE_MENUS",
                column: "RoleCode");

            migrationBuilder.AddForeignKey(
                name: "FK_PERMISSIONS_ROLES_RoleCode",
                table: "PERMISSIONS",
                column: "RoleCode",
                principalTable: "ROLES",
                principalColumn: "Code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PERMISSIONS_ROLES_RoleCode",
                table: "PERMISSIONS");

            migrationBuilder.DropTable(
                name: "ROLE_MENUS");

            migrationBuilder.DropIndex(
                name: "IX_PERMISSIONS_RoleCode",
                table: "PERMISSIONS");

            migrationBuilder.DropColumn(
                name: "RoleCode",
                table: "PERMISSIONS");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "ROLES",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "VARCHAR(100)",
                oldNullable: true);
        }
    }
}
