using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <inheritdoc />
    public partial class CreateTableUserSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_SYSTEM_USERS",
                table: "SYSTEM_USERS");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SYSTEM_COMPANIES",
                table: "SYSTEM_COMPANIES");

            migrationBuilder.RenameTable(
                name: "SYSTEM_USERS",
                newName: "USERS");

            migrationBuilder.RenameTable(
                name: "SYSTEM_COMPANIES",
                newName: "COMPANIES");

            migrationBuilder.RenameIndex(
                name: "IX_SYSTEM_USERS_Username",
                table: "USERS",
                newName: "IX_USERS_Username");

            migrationBuilder.RenameIndex(
                name: "IX_SYSTEM_USERS_Email",
                table: "USERS",
                newName: "IX_USERS_Email");

            migrationBuilder.AddPrimaryKey(
                name: "PK_USERS",
                table: "USERS",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_COMPANIES",
                table: "COMPANIES",
                column: "Code");

            migrationBuilder.CreateTable(
                name: "USER_SESSIONS",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IpAddress = table.Column<string>(type: "VARCHAR(45)", nullable: true),
                    UserAgent = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USER_SESSIONS", x => x.SessionId);
                    table.ForeignKey(
                        name: "FK_USER_SESSIONS_USERS_UserId",
                        column: x => x.UserId,
                        principalTable: "USERS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_USER_SESSIONS_UserId",
                table: "USER_SESSIONS",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "USER_SESSIONS");

            migrationBuilder.DropPrimaryKey(
                name: "PK_USERS",
                table: "USERS");

            migrationBuilder.DropPrimaryKey(
                name: "PK_COMPANIES",
                table: "COMPANIES");

            migrationBuilder.RenameTable(
                name: "USERS",
                newName: "SYSTEM_USERS");

            migrationBuilder.RenameTable(
                name: "COMPANIES",
                newName: "SYSTEM_COMPANIES");

            migrationBuilder.RenameIndex(
                name: "IX_USERS_Username",
                table: "SYSTEM_USERS",
                newName: "IX_SYSTEM_USERS_Username");

            migrationBuilder.RenameIndex(
                name: "IX_USERS_Email",
                table: "SYSTEM_USERS",
                newName: "IX_SYSTEM_USERS_Email");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SYSTEM_USERS",
                table: "SYSTEM_USERS",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SYSTEM_COMPANIES",
                table: "SYSTEM_COMPANIES",
                column: "Code");
        }
    }
}
