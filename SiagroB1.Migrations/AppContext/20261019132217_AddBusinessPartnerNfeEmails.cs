using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddBusinessPartnerNfeEmails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BUSINESS_PARTNER_NFE_EMAILS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CardCode = table.Column<string>(type: "VARCHAR(15)", nullable: false),
                    Email = table.Column<string>(type: "VARCHAR(250)", nullable: false),
                    LineNum = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BUSINESS_PARTNER_NFE_EMAILS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_BUSINESS_PARTNER_NFE_EMAILS_BUSINESS_PARTNERS_CardCode",
                        column: x => x.CardCode,
                        principalTable: "BUSINESS_PARTNERS",
                        principalColumn: "CardCode",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BUSINESS_PARTNER_NFE_EMAILS_CardCode_Email",
                table: "BUSINESS_PARTNER_NFE_EMAILS",
                columns: new[] { "CardCode", "Email" },
                unique: true);

            // Preserva o e-mail único legado como primeiro item da lista antes de descartar a coluna.
            migrationBuilder.Sql(@"
                INSERT INTO BUSINESS_PARTNER_NFE_EMAILS ([Key], CardCode, Email, LineNum)
                SELECT NEWID(), CardCode, LTRIM(RTRIM(NfeEmail)), 0
                FROM BUSINESS_PARTNERS
                WHERE NfeEmail IS NOT NULL AND LTRIM(RTRIM(NfeEmail)) <> ''");

            migrationBuilder.DropColumn(
                name: "NfeEmail",
                table: "BUSINESS_PARTNERS");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NfeEmail",
                table: "BUSINESS_PARTNERS",
                type: "VARCHAR(250)",
                nullable: true);

            // Volta apenas o primeiro e-mail (LineNum 0); os demais se perdem no rollback.
            migrationBuilder.Sql(@"
                UPDATE bp SET NfeEmail = e.Email
                FROM BUSINESS_PARTNERS bp
                JOIN BUSINESS_PARTNER_NFE_EMAILS e ON e.CardCode = bp.CardCode AND e.LineNum = 0");

            migrationBuilder.DropTable(
                name: "BUSINESS_PARTNER_NFE_EMAILS");
        }
    }
}
