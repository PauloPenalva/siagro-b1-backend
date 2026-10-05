using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class CreateIbsCbsRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IBS_CBS_RATES",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StartDate = table.Column<DateOnly>(type: "DATE", nullable: false),
                    CbsRate = table.Column<decimal>(type: "DECIMAL(7,4)", nullable: false),
                    IbsStateRate = table.Column<decimal>(type: "DECIMAL(7,4)", nullable: false),
                    IbsMunicipalRate = table.Column<decimal>(type: "DECIMAL(7,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IBS_CBS_RATES", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IBS_CBS_RATES_StartDate",
                table: "IBS_CBS_RATES",
                column: "StartDate",
                unique: true);

            // Vigência de 2026 (ano de teste da reforma). Idempotente, como as demais sementes.
            migrationBuilder.Sql(
                "IF NOT EXISTS (SELECT 1 FROM IBS_CBS_RATES WHERE StartDate = '2026-01-01') " +
                "INSERT INTO IBS_CBS_RATES (StartDate, CbsRate, IbsStateRate, IbsMunicipalRate) " +
                "VALUES ('2026-01-01', 0.9, 0.1, 0);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IBS_CBS_RATES");
        }
    }
}
