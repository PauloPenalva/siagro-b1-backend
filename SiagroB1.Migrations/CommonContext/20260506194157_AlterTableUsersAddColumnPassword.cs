using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <inheritdoc />
    public partial class AlterTableUsersAddColumnPassword : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "PasswordHash",
                table: "USERS",
                type: "VARCHAR(256)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "VARCHAR(256)");

            migrationBuilder.AddColumn<string>(
                name: "Password",
                table: "USERS",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Password",
                table: "USERS");

            migrationBuilder.AlterColumn<string>(
                name: "PasswordHash",
                table: "USERS",
                type: "VARCHAR(256)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "VARCHAR(256)",
                oldNullable: true);
        }
    }
}
