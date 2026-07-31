using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <inheritdoc />
    public partial class AddPasswordResetAndUserProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Password",
                table: "USERS");

            migrationBuilder.AddColumn<byte[]>(
                name: "PhotoContent",
                table: "USERS",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhotoContentType",
                table: "USERS",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Theme",
                table: "USERS",
                type: "VARCHAR(30)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PASSWORD_RESET_TOKENS",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<string>(type: "VARCHAR(64)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RequestIp = table.Column<string>(type: "VARCHAR(45)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PASSWORD_RESET_TOKENS", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PASSWORD_RESET_TOKENS_USERS_UserId",
                        column: x => x.UserId,
                        principalTable: "USERS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PASSWORD_RESET_TOKENS_TokenHash",
                table: "PASSWORD_RESET_TOKENS",
                column: "TokenHash");

            migrationBuilder.CreateIndex(
                name: "IX_PASSWORD_RESET_TOKENS_UserId",
                table: "PASSWORD_RESET_TOKENS",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PASSWORD_RESET_TOKENS");

            migrationBuilder.DropColumn(
                name: "PhotoContent",
                table: "USERS");

            migrationBuilder.DropColumn(
                name: "PhotoContentType",
                table: "USERS");

            migrationBuilder.DropColumn(
                name: "Theme",
                table: "USERS");

            migrationBuilder.AddColumn<string>(
                name: "Password",
                table: "USERS",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
