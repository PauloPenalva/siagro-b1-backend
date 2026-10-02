using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddBranchIssuerFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Complement",
                table: "BRANCHS",
                type: "VARCHAR(60)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "District",
                table: "BRANCHS",
                type: "VARCHAR(60)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalName",
                table: "BRANCHS",
                type: "VARCHAR(60)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MunicipalityCode",
                table: "BRANCHS",
                type: "VARCHAR(7)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "BRANCHS",
                type: "VARCHAR(14)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StateRegistration",
                table: "BRANCHS",
                type: "VARCHAR(14)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Street",
                table: "BRANCHS",
                type: "VARCHAR(60)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StreetNumber",
                table: "BRANCHS",
                type: "VARCHAR(60)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TradeName",
                table: "BRANCHS",
                type: "VARCHAR(60)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZipCode",
                table: "BRANCHS",
                type: "VARCHAR(8)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BRANCHS_MunicipalityCode",
                table: "BRANCHS",
                column: "MunicipalityCode");

            migrationBuilder.AddForeignKey(
                name: "FK_BRANCHS_MUNICIPALITIES_MunicipalityCode",
                table: "BRANCHS",
                column: "MunicipalityCode",
                principalTable: "MUNICIPALITIES",
                principalColumn: "Code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BRANCHS_MUNICIPALITIES_MunicipalityCode",
                table: "BRANCHS");

            migrationBuilder.DropIndex(
                name: "IX_BRANCHS_MunicipalityCode",
                table: "BRANCHS");

            migrationBuilder.DropColumn(
                name: "Complement",
                table: "BRANCHS");

            migrationBuilder.DropColumn(
                name: "District",
                table: "BRANCHS");

            migrationBuilder.DropColumn(
                name: "LegalName",
                table: "BRANCHS");

            migrationBuilder.DropColumn(
                name: "MunicipalityCode",
                table: "BRANCHS");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "BRANCHS");

            migrationBuilder.DropColumn(
                name: "StateRegistration",
                table: "BRANCHS");

            migrationBuilder.DropColumn(
                name: "Street",
                table: "BRANCHS");

            migrationBuilder.DropColumn(
                name: "StreetNumber",
                table: "BRANCHS");

            migrationBuilder.DropColumn(
                name: "TradeName",
                table: "BRANCHS");

            migrationBuilder.DropColumn(
                name: "ZipCode",
                table: "BRANCHS");
        }
    }
}
