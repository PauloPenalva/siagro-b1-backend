using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <inheritdoc />
    public partial class CreateTableProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PERMISSIONS",
                columns: table => new
                {
                    Code = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    Description = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PERMISSIONS", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "PROFILES",
                columns: table => new
                {
                    Code = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    Description = table.Column<string>(type: "VARCHAR(254)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PROFILES", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "ROLES",
                columns: table => new
                {
                    Code = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ROLES", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "USERS_PROFILES",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProfileCode = table.Column<string>(type: "VARCHAR(50)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USERS_PROFILES", x => x.Id);
                    table.ForeignKey(
                        name: "FK_USERS_PROFILES_PROFILES_ProfileCode",
                        column: x => x.ProfileCode,
                        principalTable: "PROFILES",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_USERS_PROFILES_USERS_UserId",
                        column: x => x.UserId,
                        principalTable: "USERS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PROFILES_ROLES",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProfileCode = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    RoleCode = table.Column<string>(type: "VARCHAR(50)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PROFILES_ROLES", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PROFILES_ROLES_PROFILES_ProfileCode",
                        column: x => x.ProfileCode,
                        principalTable: "PROFILES",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PROFILES_ROLES_ROLES_RoleCode",
                        column: x => x.RoleCode,
                        principalTable: "ROLES",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PROFILES_ROLES_ProfileCode_RoleCode",
                table: "PROFILES_ROLES",
                columns: new[] { "ProfileCode", "RoleCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PROFILES_ROLES_RoleCode",
                table: "PROFILES_ROLES",
                column: "RoleCode");

            migrationBuilder.CreateIndex(
                name: "IX_USERS_PROFILES_ProfileCode",
                table: "USERS_PROFILES",
                column: "ProfileCode");

            migrationBuilder.CreateIndex(
                name: "IX_USERS_PROFILES_UserId_ProfileCode",
                table: "USERS_PROFILES",
                columns: new[] { "UserId", "ProfileCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PERMISSIONS");

            migrationBuilder.DropTable(
                name: "PROFILES_ROLES");

            migrationBuilder.DropTable(
                name: "USERS_PROFILES");

            migrationBuilder.DropTable(
                name: "ROLES");

            migrationBuilder.DropTable(
                name: "PROFILES");
        }
    }
}
