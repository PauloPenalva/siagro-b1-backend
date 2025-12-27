using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <inheritdoc />
    public partial class CreateTableSystemUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_COMPANIES",
                table: "COMPANIES");

            migrationBuilder.RenameTable(
                name: "COMPANIES",
                newName: "SYSTEM_COMPANIES");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SYSTEM_COMPANIES",
                table: "SYSTEM_COMPANIES",
                column: "Code");

            migrationBuilder.CreateTable(
                name: "SYSTEM_USERS",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Username = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    PasswordHash = table.Column<string>(type: "VARCHAR(256)", nullable: false),
                    FullName = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    Email = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsAdmin = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SYSTEM_USERS", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SYSTEM_USERS_Email",
                table: "SYSTEM_USERS",
                column: "Email",
                unique: true,
                filter: "[Email] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SYSTEM_USERS_Username",
                table: "SYSTEM_USERS",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SYSTEM_USERS");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SYSTEM_COMPANIES",
                table: "SYSTEM_COMPANIES");

            migrationBuilder.RenameTable(
                name: "SYSTEM_COMPANIES",
                newName: "COMPANIES");

            migrationBuilder.AddPrimaryKey(
                name: "PK_COMPANIES",
                table: "COMPANIES",
                column: "Code");
        }
    }
}
