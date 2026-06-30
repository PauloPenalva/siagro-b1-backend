using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <inheritdoc />
    public partial class AlterTableRolesAlterFKRolePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PERMISSIONS_ROLES_RoleCode",
                table: "PERMISSIONS");

            migrationBuilder.DropIndex(
                name: "IX_PERMISSIONS_RoleCode",
                table: "PERMISSIONS");

            migrationBuilder.DropColumn(
                name: "RoleCode",
                table: "PERMISSIONS");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RoleCode",
                table: "PERMISSIONS",
                type: "VARCHAR(50)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PERMISSIONS_RoleCode",
                table: "PERMISSIONS",
                column: "RoleCode");

            migrationBuilder.AddForeignKey(
                name: "FK_PERMISSIONS_ROLES_RoleCode",
                table: "PERMISSIONS",
                column: "RoleCode",
                principalTable: "ROLES",
                principalColumn: "Code");
        }
    }
}
