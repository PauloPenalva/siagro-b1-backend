using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class CreateBranchNfeSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BRANCH_NFE_SETTINGS",
                columns: table => new
                {
                    BranchCode = table.Column<string>(type: "VARCHAR(14)", nullable: false),
                    Environment = table.Column<int>(type: "int", nullable: false),
                    Series = table.Column<int>(type: "int", nullable: false),
                    NextNumber = table.Column<int>(type: "int", nullable: false),
                    CertificatePfx = table.Column<byte[]>(type: "VARBINARY(MAX)", nullable: true),
                    CertificatePasswordCipher = table.Column<byte[]>(type: "VARBINARY(512)", nullable: true),
                    CertificateSubject = table.Column<string>(type: "VARCHAR(250)", nullable: true),
                    CertificateTaxId = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    CertificateValidUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BRANCH_NFE_SETTINGS", x => x.BranchCode);
                    table.ForeignKey(
                        name: "FK_BRANCH_NFE_SETTINGS_BRANCHS_BranchCode",
                        column: x => x.BranchCode,
                        principalTable: "BRANCHS",
                        principalColumn: "Code");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BRANCH_NFE_SETTINGS");
        }
    }
}
