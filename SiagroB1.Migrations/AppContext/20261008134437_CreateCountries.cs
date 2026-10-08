using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class CreateCountries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "COUNTRIES",
                columns: table => new
                {
                    Code = table.Column<string>(type: "VARCHAR(2)", nullable: false),
                    Name = table.Column<string>(type: "NVARCHAR(100)", nullable: false),
                    BacenCode = table.Column<string>(type: "VARCHAR(4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_COUNTRIES", x => x.Code);
                });

            migrationBuilder.CreateIndex(
                name: "IX_COUNTRIES_BacenCode",
                table: "COUNTRIES",
                column: "BacenCode",
                unique: true);

            // Países da tabela do BACEN com o ISO-2 (ver CountrySeed).
            foreach (var sql in SiagroB1.Migrations.Seeds.CountrySeed.InsertBatches())
            {
                migrationBuilder.Sql(sql);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "COUNTRIES");
        }
    }
}
