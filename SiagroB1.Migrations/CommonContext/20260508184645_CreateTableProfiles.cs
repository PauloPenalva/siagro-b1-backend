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
                name: "PROFILES",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    Description = table.Column<string>(type: "VARCHAR(254)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PROFILES", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PROFILES_ROLES",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "VARCHAR(100)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PROFILES_ROLES", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PROFILES_ROLES_PROFILES_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "PROFILES",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PROFILES_ROLES_ProfileId",
                table: "PROFILES_ROLES",
                column: "ProfileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PROFILES_ROLES");

            migrationBuilder.DropTable(
                name: "PROFILES");
        }
    }
}
