using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class CreateMunicipalities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MUNICIPALITIES",
                columns: table => new
                {
                    Code = table.Column<string>(type: "VARCHAR(7)", nullable: false),
                    Name = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    StateAbbreviation = table.Column<string>(type: "VARCHAR(2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MUNICIPALITIES", x => x.Code);
                });

            // 5.570 municípios do IBGE, a partir do recurso embarcado (ver MunicipalitySeed).
            foreach (var sql in SiagroB1.Migrations.Seeds.MunicipalitySeed.InsertBatches())
            {
                migrationBuilder.Sql(sql);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MUNICIPALITIES");
        }
    }
}
