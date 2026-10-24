using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddEcdSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ECD_SETTINGS",
                columns: table => new
                {
                    BranchCode = table.Column<string>(type: "VARCHAR(14)", nullable: false),
                    BookNumber = table.Column<int>(type: "int", nullable: false),
                    HasNire = table.Column<bool>(type: "bit", nullable: false),
                    Nire = table.Column<string>(type: "VARCHAR(11)", nullable: true),
                    ConstitutiveActsDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LargeCompany = table.Column<bool>(type: "bit", nullable: false),
                    ReferentialPlanCode = table.Column<string>(type: "VARCHAR(2)", nullable: false),
                    SignerName = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    SignerCpf = table.Column<string>(type: "VARCHAR(11)", nullable: false),
                    SignerQualificationCode = table.Column<string>(type: "VARCHAR(3)", nullable: false),
                    SignerEmail = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    SignerPhone = table.Column<string>(type: "VARCHAR(11)", nullable: true),
                    AccountantCrcSequence = table.Column<string>(type: "VARCHAR(50)", nullable: true),
                    AccountantCrcValidUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ECD_SETTINGS", x => x.BranchCode);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ECD_SETTINGS");
        }
    }
}
