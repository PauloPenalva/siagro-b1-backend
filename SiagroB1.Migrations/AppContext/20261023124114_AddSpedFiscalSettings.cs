using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddSpedFiscalSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SPED_FISCAL_SETTINGS",
                columns: table => new
                {
                    BranchCode = table.Column<string>(type: "VARCHAR(14)", nullable: false),
                    Profile = table.Column<string>(type: "VARCHAR(1)", nullable: false),
                    ActivityType = table.Column<int>(type: "int", nullable: false),
                    AccountantName = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    AccountantCpf = table.Column<string>(type: "VARCHAR(11)", nullable: false),
                    AccountantCrc = table.Column<string>(type: "VARCHAR(15)", nullable: false),
                    AccountantCnpj = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    AccountantZipCode = table.Column<string>(type: "VARCHAR(8)", nullable: true),
                    AccountantStreet = table.Column<string>(type: "VARCHAR(60)", nullable: true),
                    AccountantNumber = table.Column<string>(type: "VARCHAR(10)", nullable: true),
                    AccountantComplement = table.Column<string>(type: "VARCHAR(60)", nullable: true),
                    AccountantDistrict = table.Column<string>(type: "VARCHAR(60)", nullable: true),
                    AccountantPhone = table.Column<string>(type: "VARCHAR(11)", nullable: true),
                    AccountantEmail = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    AccountantMunicipalityCode = table.Column<string>(type: "VARCHAR(7)", nullable: true),
                    IcmsRevenueCode = table.Column<string>(type: "VARCHAR(30)", nullable: false),
                    IcmsDueDay = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SPED_FISCAL_SETTINGS", x => x.BranchCode);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SPED_FISCAL_SETTINGS");
        }
    }
}
