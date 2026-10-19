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

            // Preserva os e-mails legados antes de descartar a coluna. O campo antigo podia guardar vários
            // endereços separados por ';', ',', espaço, TAB ou quebra de linha: separa, remove brancos e
            // duplicados (sem diferenciar maiúsculas, mantendo a primeira ocorrência), limita a 10 por parceiro.
            // CTE recursiva (sem STRING_SPLIT de 3 argumentos) para funcionar em níveis de compatibilidade < 160.
            migrationBuilder.Sql(@"
                WITH src AS (
                    SELECT CardCode,
                           REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(NfeEmail, ',', ';'), ' ', ';'),
                                   CHAR(9), ';'), CHAR(13), ';'), CHAR(10), ';') + ';' AS S
                    FROM BUSINESS_PARTNERS
                    WHERE NfeEmail IS NOT NULL
                ),
                parts AS (
                    SELECT CardCode, 0 AS Ord,
                           CAST(LEFT(S, CHARINDEX(';', S) - 1) AS NVARCHAR(500)) AS Item,
                           CAST(SUBSTRING(S, CHARINDEX(';', S) + 1, LEN(S)) AS NVARCHAR(500)) AS Rest
                    FROM src
                    UNION ALL
                    SELECT CardCode, Ord + 1,
                           CAST(LEFT(Rest, CHARINDEX(';', Rest) - 1) AS NVARCHAR(500)),
                           CAST(SUBSTRING(Rest, CHARINDEX(';', Rest) + 1, LEN(Rest)) AS NVARCHAR(500))
                    FROM parts
                    WHERE Rest <> ''
                ),
                trimmed AS (
                    SELECT CardCode, Ord, LTRIM(RTRIM(Item)) AS Item
                    FROM parts
                    WHERE LTRIM(RTRIM(Item)) <> ''
                ),
                cleaned AS (
                    SELECT CardCode, Item, Ord,
                           ROW_NUMBER() OVER (PARTITION BY CardCode, Item COLLATE Latin1_General_CI_AI
                                              ORDER BY Ord) AS Dup
                    FROM trimmed
                ),
                numbered AS (
                    SELECT CardCode, Item,
                           ROW_NUMBER() OVER (PARTITION BY CardCode ORDER BY Ord) - 1 AS LineNum
                    FROM cleaned
                    WHERE Dup = 1
                )
                INSERT INTO BUSINESS_PARTNER_NFE_EMAILS ([Key], CardCode, Email, LineNum)
                SELECT NEWID(), CardCode, LEFT(Item, 250), LineNum
                FROM numbered
                WHERE LineNum < 10
                OPTION (MAXRECURSION 0)");

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

            // Devolve todos os e-mails ao campo antigo, unidos por ';' na ordem de LineNum.
            migrationBuilder.Sql(@"
                UPDATE bp SET NfeEmail = LEFT(x.Joined, 250)
                FROM BUSINESS_PARTNERS bp
                CROSS APPLY (
                    SELECT STUFF((SELECT ';' + e.Email
                                  FROM BUSINESS_PARTNER_NFE_EMAILS e
                                  WHERE e.CardCode = bp.CardCode
                                  ORDER BY e.LineNum
                                  FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 1, '') AS Joined
                ) x
                WHERE x.Joined IS NOT NULL");

            migrationBuilder.DropTable(
                name: "BUSINESS_PARTNER_NFE_EMAILS");
        }
    }
}
