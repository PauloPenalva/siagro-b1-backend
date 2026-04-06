using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class CreateTableEnvironmentSetup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ENVIRONMENT_SETUP",
                columns: table => new
                {
                    VARCHAR4NOTNULL = table.Column<string>(name: "VARCHAR(4) NOT NULL", type: "nvarchar(450)", nullable: false),
                    DefaultUoM = table.Column<string>(type: "VARCHAR(4)", nullable: true),
                    DefaultCurrency = table.Column<int>(type: "int", nullable: true),
                    DefaultFreightUoM = table.Column<string>(type: "VARCHAR(4)", nullable: true),
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ENVIRONMENT_SETUP");
        }
    }
}
