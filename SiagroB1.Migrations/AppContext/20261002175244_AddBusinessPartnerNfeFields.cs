using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddBusinessPartnerNfeFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Complement",
                table: "BUSINESS_PARTNERS_ADDRESSES",
                type: "VARCHAR(60)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MunicipalityCode",
                table: "BUSINESS_PARTNERS_ADDRESSES",
                type: "VARCHAR(7)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StreetNumber",
                table: "BUSINESS_PARTNERS_ADDRESSES",
                type: "VARCHAR(60)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NfeEmail",
                table: "BUSINESS_PARTNERS",
                type: "VARCHAR(250)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentConditionCode",
                table: "BUSINESS_PARTNERS",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "BUSINESS_PARTNERS",
                type: "VARCHAR(14)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StateRegistration",
                table: "BUSINESS_PARTNERS",
                type: "VARCHAR(14)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StateRegistrationIndicator",
                table: "BUSINESS_PARTNERS",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BUSINESS_PARTNERS_ADDRESSES_MunicipalityCode",
                table: "BUSINESS_PARTNERS_ADDRESSES",
                column: "MunicipalityCode");

            migrationBuilder.AddForeignKey(
                name: "FK_BUSINESS_PARTNERS_ADDRESSES_MUNICIPALITIES_MunicipalityCode",
                table: "BUSINESS_PARTNERS_ADDRESSES",
                column: "MunicipalityCode",
                principalTable: "MUNICIPALITIES",
                principalColumn: "Code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BUSINESS_PARTNERS_ADDRESSES_MUNICIPALITIES_MunicipalityCode",
                table: "BUSINESS_PARTNERS_ADDRESSES");

            migrationBuilder.DropIndex(
                name: "IX_BUSINESS_PARTNERS_ADDRESSES_MunicipalityCode",
                table: "BUSINESS_PARTNERS_ADDRESSES");

            migrationBuilder.DropColumn(
                name: "Complement",
                table: "BUSINESS_PARTNERS_ADDRESSES");

            migrationBuilder.DropColumn(
                name: "MunicipalityCode",
                table: "BUSINESS_PARTNERS_ADDRESSES");

            migrationBuilder.DropColumn(
                name: "StreetNumber",
                table: "BUSINESS_PARTNERS_ADDRESSES");

            migrationBuilder.DropColumn(
                name: "NfeEmail",
                table: "BUSINESS_PARTNERS");

            migrationBuilder.DropColumn(
                name: "PaymentConditionCode",
                table: "BUSINESS_PARTNERS");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "BUSINESS_PARTNERS");

            migrationBuilder.DropColumn(
                name: "StateRegistration",
                table: "BUSINESS_PARTNERS");

            migrationBuilder.DropColumn(
                name: "StateRegistrationIndicator",
                table: "BUSINESS_PARTNERS");
        }
    }
}
