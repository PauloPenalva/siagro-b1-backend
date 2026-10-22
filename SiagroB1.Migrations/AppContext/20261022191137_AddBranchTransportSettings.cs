using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddBranchTransportSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BRANCH_TRANSPORT_SETTINGS",
                columns: table => new
                {
                    BranchCode = table.Column<string>(type: "VARCHAR(14)", nullable: false),
                    CompanyRntrc = table.Column<string>(type: "VARCHAR(8)", nullable: true),
                    InsuranceResponsible = table.Column<int>(type: "int", nullable: true),
                    InsurerTaxId = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    InsurerName = table.Column<string>(type: "VARCHAR(115)", nullable: true),
                    PolicyNumber = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    AverbaEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AverbaUser = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    AverbaPasswordCipher = table.Column<byte[]>(type: "VARBINARY(512)", nullable: true),
                    AverbaCode = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    AverbaEnvironment = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BRANCH_TRANSPORT_SETTINGS", x => x.BranchCode);
                    table.ForeignKey(
                        name: "FK_BRANCH_TRANSPORT_SETTINGS_BRANCHS_BranchCode",
                        column: x => x.BranchCode,
                        principalTable: "BRANCHS",
                        principalColumn: "Code");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BRANCH_TRANSPORT_SETTINGS");
        }
    }
}
