using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <inheritdoc />
    public partial class AlterTableMenuItemsDropColumnId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MENU_ITEMS_MENU_ITEMS_ParentId",
                table: "MENU_ITEMS");

            migrationBuilder.DropForeignKey(
                name: "FK_ROLE_MENUS_MENU_ITEMS_MenuItemId",
                table: "ROLE_MENUS");

            migrationBuilder.DropIndex(
                name: "IX_ROLE_MENUS_MenuItemId",
                table: "ROLE_MENUS");

            migrationBuilder.DropPrimaryKey(
                name: "PK_MENU_ITEMS",
                table: "MENU_ITEMS");

            migrationBuilder.DropIndex(
                name: "IX_MENU_ITEMS_ParentId",
                table: "MENU_ITEMS");

            migrationBuilder.DropColumn(
                name: "MenuItemId",
                table: "ROLE_MENUS");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "MENU_ITEMS");

            migrationBuilder.DropColumn(
                name: "ParentId",
                table: "MENU_ITEMS");

            migrationBuilder.AddColumn<string>(
                name: "MenuItemKey",
                table: "ROLE_MENUS",
                type: "VARCHAR(50)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "Key",
                table: "MENU_ITEMS",
                type: "VARCHAR(50)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "VARCHAR(50)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParentKey",
                table: "MENU_ITEMS",
                type: "VARCHAR(50)",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_MENU_ITEMS",
                table: "MENU_ITEMS",
                column: "Key");

            migrationBuilder.CreateIndex(
                name: "IX_ROLE_MENUS_MenuItemKey",
                table: "ROLE_MENUS",
                column: "MenuItemKey");

            migrationBuilder.CreateIndex(
                name: "IX_MENU_ITEMS_Key",
                table: "MENU_ITEMS",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MENU_ITEMS_ParentKey",
                table: "MENU_ITEMS",
                column: "ParentKey");

            migrationBuilder.AddForeignKey(
                name: "FK_MENU_ITEMS_MENU_ITEMS_ParentKey",
                table: "MENU_ITEMS",
                column: "ParentKey",
                principalTable: "MENU_ITEMS",
                principalColumn: "Key");

            migrationBuilder.AddForeignKey(
                name: "FK_ROLE_MENUS_MENU_ITEMS_MenuItemKey",
                table: "ROLE_MENUS",
                column: "MenuItemKey",
                principalTable: "MENU_ITEMS",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MENU_ITEMS_MENU_ITEMS_ParentKey",
                table: "MENU_ITEMS");

            migrationBuilder.DropForeignKey(
                name: "FK_ROLE_MENUS_MENU_ITEMS_MenuItemKey",
                table: "ROLE_MENUS");

            migrationBuilder.DropIndex(
                name: "IX_ROLE_MENUS_MenuItemKey",
                table: "ROLE_MENUS");

            migrationBuilder.DropPrimaryKey(
                name: "PK_MENU_ITEMS",
                table: "MENU_ITEMS");

            migrationBuilder.DropIndex(
                name: "IX_MENU_ITEMS_Key",
                table: "MENU_ITEMS");

            migrationBuilder.DropIndex(
                name: "IX_MENU_ITEMS_ParentKey",
                table: "MENU_ITEMS");

            migrationBuilder.DropColumn(
                name: "MenuItemKey",
                table: "ROLE_MENUS");

            migrationBuilder.DropColumn(
                name: "ParentKey",
                table: "MENU_ITEMS");

            migrationBuilder.AddColumn<Guid>(
                name: "MenuItemId",
                table: "ROLE_MENUS",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<string>(
                name: "Key",
                table: "MENU_ITEMS",
                type: "VARCHAR(50)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "VARCHAR(50)");

            migrationBuilder.AddColumn<Guid>(
                name: "Id",
                table: "MENU_ITEMS",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ParentId",
                table: "MENU_ITEMS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_MENU_ITEMS",
                table: "MENU_ITEMS",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_ROLE_MENUS_MenuItemId",
                table: "ROLE_MENUS",
                column: "MenuItemId");

            migrationBuilder.CreateIndex(
                name: "IX_MENU_ITEMS_ParentId",
                table: "MENU_ITEMS",
                column: "ParentId");

            migrationBuilder.AddForeignKey(
                name: "FK_MENU_ITEMS_MENU_ITEMS_ParentId",
                table: "MENU_ITEMS",
                column: "ParentId",
                principalTable: "MENU_ITEMS",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ROLE_MENUS_MENU_ITEMS_MenuItemId",
                table: "ROLE_MENUS",
                column: "MenuItemId",
                principalTable: "MENU_ITEMS",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
