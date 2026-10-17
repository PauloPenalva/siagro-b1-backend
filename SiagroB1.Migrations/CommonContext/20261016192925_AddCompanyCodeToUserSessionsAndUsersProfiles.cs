using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <inheritdoc />
    public partial class AddCompanyCodeToUserSessionsAndUsersProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_USERS_PROFILES_UserId_ProfileCode",
                table: "USERS_PROFILES");

            migrationBuilder.AddColumn<string>(
                name: "CompanyCode",
                table: "USERS_PROFILES",
                type: "VARCHAR(4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompanyCode",
                table: "USER_SESSIONS",
                type: "VARCHAR(4)",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ConnectionString",
                table: "COMPANIES",
                type: "VARCHAR(500)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "VARCHAR(200)");

            migrationBuilder.CreateIndex(
                name: "IX_USERS_PROFILES_CompanyCode",
                table: "USERS_PROFILES",
                column: "CompanyCode");

            migrationBuilder.CreateIndex(
                name: "IX_USERS_PROFILES_UserId_ProfileCode_CompanyCode",
                table: "USERS_PROFILES",
                columns: new[] { "UserId", "ProfileCode", "CompanyCode" },
                unique: true,
                filter: "[CompanyCode] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_USERS_PROFILES_COMPANIES_CompanyCode",
                table: "USERS_PROFILES",
                column: "CompanyCode",
                principalTable: "COMPANIES",
                principalColumn: "Code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_USERS_PROFILES_COMPANIES_CompanyCode",
                table: "USERS_PROFILES");

            migrationBuilder.DropIndex(
                name: "IX_USERS_PROFILES_CompanyCode",
                table: "USERS_PROFILES");

            migrationBuilder.DropIndex(
                name: "IX_USERS_PROFILES_UserId_ProfileCode_CompanyCode",
                table: "USERS_PROFILES");

            migrationBuilder.DropColumn(
                name: "CompanyCode",
                table: "USERS_PROFILES");

            migrationBuilder.DropColumn(
                name: "CompanyCode",
                table: "USER_SESSIONS");

            migrationBuilder.AlterColumn<string>(
                name: "ConnectionString",
                table: "COMPANIES",
                type: "VARCHAR(200)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "VARCHAR(500)");

            migrationBuilder.CreateIndex(
                name: "IX_USERS_PROFILES_UserId_ProfileCode",
                table: "USERS_PROFILES",
                columns: new[] { "UserId", "ProfileCode" },
                unique: true);
        }
    }
}
