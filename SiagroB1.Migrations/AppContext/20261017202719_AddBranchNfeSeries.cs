using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddBranchNfeSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BRANCH_NFE_SERIES",
                columns: table => new
                {
                    BranchCode = table.Column<string>(type: "VARCHAR(14)", nullable: false),
                    Series = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "VARCHAR(60)", nullable: false),
                    NextNumber = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BRANCH_NFE_SERIES", x => new { x.BranchCode, x.Series });
                    table.ForeignKey(
                        name: "FK_BRANCH_NFE_SERIES_BRANCH_NFE_SETTINGS_BranchCode",
                        column: x => x.BranchCode,
                        principalTable: "BRANCH_NFE_SETTINGS",
                        principalColumn: "BranchCode",
                        onDelete: ReferentialAction.Cascade);
                });

            // O contador de cada filial vira a linha da sua série (a que já era a padrão).
            migrationBuilder.Sql(
                "INSERT INTO BRANCH_NFE_SERIES (BranchCode, Series, Description, NextNumber) " +
                "SELECT BranchCode, Series, 'Padrão', NextNumber FROM BRANCH_NFE_SETTINGS;");

            migrationBuilder.RenameColumn(
                name: "Series",
                table: "BRANCH_NFE_SETTINGS",
                newName: "DefaultSeries");

            migrationBuilder.DropColumn(
                name: "NextNumber",
                table: "BRANCH_NFE_SETTINGS");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NextNumber",
                table: "BRANCH_NFE_SETTINGS",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.Sql(
                "UPDATE s SET NextNumber = x.NextNumber FROM BRANCH_NFE_SETTINGS s " +
                "JOIN BRANCH_NFE_SERIES x ON x.BranchCode = s.BranchCode AND x.Series = s.DefaultSeries;");

            migrationBuilder.RenameColumn(
                name: "DefaultSeries",
                table: "BRANCH_NFE_SETTINGS",
                newName: "Series");

            migrationBuilder.DropTable(
                name: "BRANCH_NFE_SERIES");
        }
    }
}
