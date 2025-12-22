using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AlterTableDocNumbersAddColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BranchCode",
                table: "DOC_NUMBERS",
                type: "VARCHAR(14)",
                nullable: true)
                .Annotation("Relational:ColumnOrder", 1);

            migrationBuilder.AddColumn<bool>(
                name: "Inactive",
                table: "DOC_NUMBERS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsManual",
                table: "DOC_NUMBERS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Prefix",
                table: "DOC_NUMBERS",
                type: "VARCHAR(50)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Suffix",
                table: "DOC_NUMBERS",
                type: "VARCHAR(50)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DOC_NUMBERS_BranchCode",
                table: "DOC_NUMBERS",
                column: "BranchCode");

            migrationBuilder.AddForeignKey(
                name: "FK_DOC_NUMBERS_BRANCHS_BranchCode",
                table: "DOC_NUMBERS",
                column: "BranchCode",
                principalTable: "BRANCHS",
                principalColumn: "Code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DOC_NUMBERS_BRANCHS_BranchCode",
                table: "DOC_NUMBERS");

            migrationBuilder.DropIndex(
                name: "IX_DOC_NUMBERS_BranchCode",
                table: "DOC_NUMBERS");

            migrationBuilder.DropColumn(
                name: "BranchCode",
                table: "DOC_NUMBERS");

            migrationBuilder.DropColumn(
                name: "Inactive",
                table: "DOC_NUMBERS");

            migrationBuilder.DropColumn(
                name: "IsManual",
                table: "DOC_NUMBERS");

            migrationBuilder.DropColumn(
                name: "Prefix",
                table: "DOC_NUMBERS");

            migrationBuilder.DropColumn(
                name: "Suffix",
                table: "DOC_NUMBERS");
        }
    }
}
