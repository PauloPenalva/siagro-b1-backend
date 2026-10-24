using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddEcfTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ECF_ADJUSTMENTS",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchCode = table.Column<string>(type: "VARCHAR(14)", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Period = table.Column<int>(type: "int", nullable: false),
                    Tax = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    TableCode = table.Column<string>(type: "VARCHAR(20)", nullable: false),
                    Description = table.Column<string>(type: "VARCHAR(250)", nullable: false),
                    Amount = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    LedgerAccountCode = table.Column<string>(type: "VARCHAR(50)", nullable: true),
                    PartBAccountCode = table.Column<string>(type: "VARCHAR(50)", nullable: true),
                    PartBAccountName = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ECF_ADJUSTMENTS", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ECF_PARTNERS",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchCode = table.Column<string>(type: "VARCHAR(14)", nullable: false),
                    Name = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    TaxId = table.Column<string>(type: "VARCHAR(14)", nullable: false),
                    Qualification = table.Column<string>(type: "VARCHAR(2)", nullable: false),
                    VotingCapitalPercent = table.Column<decimal>(type: "DECIMAL(5,2)", nullable: false),
                    TotalCapitalPercent = table.Column<decimal>(type: "DECIMAL(5,2)", nullable: false),
                    CountryCode = table.Column<string>(type: "VARCHAR(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ECF_PARTNERS", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ECF_SETTINGS",
                columns: table => new
                {
                    BranchCode = table.Column<string>(type: "VARCHAR(14)", nullable: false),
                    AssessmentForm = table.Column<int>(type: "int", nullable: false),
                    MainCnae = table.Column<string>(type: "VARCHAR(7)", nullable: false),
                    LegalEntityQualification = table.Column<string>(type: "VARCHAR(2)", nullable: false),
                    NatureCode = table.Column<string>(type: "VARCHAR(4)", nullable: false),
                    PreviousEcfHash = table.Column<string>(type: "VARCHAR(40)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ECF_SETTINGS", x => x.BranchCode);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ECF_ADJUSTMENTS_BranchCode_Year",
                table: "ECF_ADJUSTMENTS",
                columns: new[] { "BranchCode", "Year" });

            migrationBuilder.CreateIndex(
                name: "IX_ECF_PARTNERS_BranchCode",
                table: "ECF_PARTNERS",
                column: "BranchCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ECF_ADJUSTMENTS");

            migrationBuilder.DropTable(
                name: "ECF_PARTNERS");

            migrationBuilder.DropTable(
                name: "ECF_SETTINGS");
        }
    }
}
