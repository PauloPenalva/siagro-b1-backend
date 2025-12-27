#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class DropTableDocTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DOC_TYPES");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DOC_TYPES",
                columns: table => new
                {
                    BranchCode = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    Code = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    FirstNumber = table.Column<int>(type: "int", nullable: false),
                    LastNumber = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    NextNumber = table.Column<int>(type: "int", nullable: false),
                    Serie = table.Column<string>(type: "VARCHAR(3)", nullable: false),
                    TransactionCode = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DOC_TYPES", x => x.Code);
                    table.ForeignKey(
                        name: "FK_DOC_TYPES_BRANCHS_BranchCode",
                        column: x => x.BranchCode,
                        principalTable: "BRANCHS",
                        principalColumn: "Code");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DOC_TYPES_BranchCode",
                table: "DOC_TYPES",
                column: "BranchCode");
        }
    }
}
