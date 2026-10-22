using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddStandardPostings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "STANDARD_POSTINGS",
                columns: table => new
                {
                    Code = table.Column<string>(type: "VARCHAR(3)", nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_STANDARD_POSTINGS", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "STANDARD_POSTING_LINES",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "VARCHAR(3)", nullable: false),
                    Sequence = table.Column<string>(type: "VARCHAR(3)", nullable: false),
                    EntryType = table.Column<int>(type: "int", nullable: false),
                    DebitAccountSource = table.Column<string>(type: "VARCHAR(40)", nullable: true),
                    DebitAccountCode = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    CreditAccountSource = table.Column<string>(type: "VARCHAR(40)", nullable: true),
                    CreditAccountCode = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    ValueSource = table.Column<string>(type: "VARCHAR(40)", nullable: false),
                    HistoryTemplate = table.Column<string>(type: "VARCHAR(200)", nullable: false),
                    DebitCostCenterSource = table.Column<int>(type: "int", nullable: false),
                    DebitCostCenterCode = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    CreditCostCenterSource = table.Column<int>(type: "int", nullable: false),
                    CreditCostCenterCode = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    DebitItemSource = table.Column<int>(type: "int", nullable: false),
                    DebitItemCode = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    CreditItemSource = table.Column<int>(type: "int", nullable: false),
                    CreditItemCode = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    Active = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_STANDARD_POSTING_LINES", x => x.Id);
                    table.ForeignKey(
                        name: "FK_STANDARD_POSTING_LINES_STANDARD_POSTINGS_Code",
                        column: x => x.Code,
                        principalTable: "STANDARD_POSTINGS",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_STANDARD_POSTING_LINES_Code_Sequence",
                table: "STANDARD_POSTING_LINES",
                columns: new[] { "Code", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "STANDARD_POSTING_LINES");

            migrationBuilder.DropTable(
                name: "STANDARD_POSTINGS");
        }
    }
}
