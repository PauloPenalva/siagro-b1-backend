using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class CreateTableSystemSetup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ENVIRONMENT_SETUP");

            migrationBuilder.CreateTable(
                name: "SYSTEM_SETUP",
                columns: table => new
                {
                    Code = table.Column<string>(type: "VARCHAR(4)", nullable: false),
                    DefaultUoM = table.Column<string>(type: "VARCHAR(4)", nullable: true),
                    DefaultCurrency = table.Column<int>(type: "int", nullable: true),
                    DefaultFreightUoM = table.Column<string>(type: "VARCHAR(4)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SYSTEM_SETUP", x => x.Code);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SYSTEM_SETUP_Code",
                table: "SYSTEM_SETUP",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SYSTEM_SETUP");

            migrationBuilder.CreateTable(
                name: "ENVIRONMENT_SETUP",
                columns: table => new
                {
                    VARCHAR4NOTNULL = table.Column<string>(name: "VARCHAR(4) NOT NULL", type: "nvarchar(450)", nullable: false),
                    DefaultCurrency = table.Column<int>(type: "int", nullable: true),
                    DefaultFreightUoM = table.Column<string>(type: "VARCHAR(4)", nullable: true),
                    DefaultUoM = table.Column<string>(type: "VARCHAR(4)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ENVIRONMENT_SETUP", x => x.VARCHAR4NOTNULL);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ENVIRONMENT_SETUP_VARCHAR(4) NOT NULL",
                table: "ENVIRONMENT_SETUP",
                column: "VARCHAR(4) NOT NULL",
                unique: true);
        }
    }
}
