using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <inheritdoc />
    public partial class CreateTableUserProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PROFILES_ROLES_ProfileId",
                table: "PROFILES_ROLES");

            migrationBuilder.CreateTable(
                name: "USERS_PROFILES",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USERS_PROFILES", x => x.Id);
                    table.ForeignKey(
                        name: "FK_USERS_PROFILES_PROFILES_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "PROFILES",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_USERS_PROFILES_USERS_UserId",
                        column: x => x.UserId,
                        principalTable: "USERS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PROFILES_ROLES_ProfileId_Role",
                table: "PROFILES_ROLES",
                columns: new[] { "ProfileId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_USERS_PROFILE_ID",
                table: "USERS_PROFILES",
                columns: new[] { "UserId", "ProfileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_USERS_PROFILES_ProfileId",
                table: "USERS_PROFILES",
                column: "ProfileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "USERS_PROFILES");

            migrationBuilder.DropIndex(
                name: "IX_PROFILES_ROLES_ProfileId_Role",
                table: "PROFILES_ROLES");

            migrationBuilder.CreateIndex(
                name: "IX_PROFILES_ROLES_ProfileId",
                table: "PROFILES_ROLES",
                column: "ProfileId");
        }
    }
}
