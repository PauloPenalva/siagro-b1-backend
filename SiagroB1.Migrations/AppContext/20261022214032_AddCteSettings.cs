using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddCteSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BRANCH_CTE_SETTINGS",
                columns: table => new
                {
                    BranchCode = table.Column<string>(type: "VARCHAR(14)", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    Environment = table.Column<int>(type: "int", nullable: false),
                    DefaultSeries = table.Column<int>(type: "int", nullable: false),
                    DefaultQuantityUnit = table.Column<string>(type: "VARCHAR(2)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BRANCH_CTE_SETTINGS", x => x.BranchCode);
                    table.ForeignKey(
                        name: "FK_BRANCH_CTE_SETTINGS_BRANCHS_BranchCode",
                        column: x => x.BranchCode,
                        principalTable: "BRANCHS",
                        principalColumn: "Code");
                });

            migrationBuilder.CreateTable(
                name: "BRANCH_CTE_SERIES",
                columns: table => new
                {
                    BranchCode = table.Column<string>(type: "VARCHAR(14)", nullable: false),
                    Series = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "VARCHAR(60)", nullable: false),
                    NextNumber = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BRANCH_CTE_SERIES", x => new { x.BranchCode, x.Series });
                    table.ForeignKey(
                        name: "FK_BRANCH_CTE_SERIES_BRANCH_CTE_SETTINGS_BranchCode",
                        column: x => x.BranchCode,
                        principalTable: "BRANCH_CTE_SETTINGS",
                        principalColumn: "BranchCode",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BRANCH_CTE_SERIES");

            migrationBuilder.DropTable(
                name: "BRANCH_CTE_SETTINGS");
        }
    }
}
