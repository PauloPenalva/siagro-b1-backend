using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <inheritdoc />
    public partial class AddCompanyHomeDashboardAndLogo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShowHomeDashboard",
                table: "COMPANIES",
                type: "bit",
                nullable: false,
                // Empresas existentes continuam com o painel.
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "COMPANY_LOGOS",
                columns: table => new
                {
                    CompanyCode = table.Column<string>(type: "VARCHAR(4)", nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    ContentType = table.Column<string>(type: "VARCHAR(50)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_COMPANY_LOGOS", x => x.CompanyCode);
                    table.ForeignKey(
                        name: "FK_COMPANY_LOGOS_COMPANIES_CompanyCode",
                        column: x => x.CompanyCode,
                        principalTable: "COMPANIES",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "COMPANY_LOGOS");

            migrationBuilder.DropColumn(
                name: "ShowHomeDashboard",
                table: "COMPANIES");
        }
    }
}
