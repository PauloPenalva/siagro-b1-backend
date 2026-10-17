using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class SeedWithholdingTaxCreditors : Migration
    {
        /// <summary>
        /// Semeia os três órgãos arrecadadores como parceiros fornecedores (CardType 'S') e o credor/vencimento de cada tributo
        /// retido (spec 2026-10-17 §2). TaxType: 0 ISS, 1 IRRF, 2 INSS, 3 PIS, 4 COFINS, 5 CSLL. Idempotente: cada parceiro e
        /// cada configuração só entra se ainda não existir, então o que o usuário já completou não é tocado.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO BUSINESS_PARTNERS (CardCode, CardName, CardType)
                SELECT v.CardCode, v.CardName, 'S'
                FROM (VALUES
                    ('TRIB-FEDERAL', 'RECEITA FEDERAL DO BRASIL'),
                    ('TRIB-ESTADUAL', 'SECRETARIA DA FAZENDA ESTADUAL'),
                    ('TRIB-MUNICIPAL', 'PREFEITURA MUNICIPAL')) AS v (CardCode, CardName)
                WHERE NOT EXISTS (SELECT 1 FROM BUSINESS_PARTNERS bp WHERE bp.CardCode = v.CardCode);

                INSERT INTO WITHHOLDING_TAX_SETTINGS (TaxType, CardCode, CardName, DueDay)
                SELECT v.TaxType, v.CardCode, bp.CardName, v.DueDay
                FROM (VALUES
                    (0, 'TRIB-MUNICIPAL', 10),
                    (1, 'TRIB-FEDERAL', 20),
                    (2, 'TRIB-FEDERAL', 20),
                    (3, 'TRIB-FEDERAL', 20),
                    (4, 'TRIB-FEDERAL', 20),
                    (5, 'TRIB-FEDERAL', 20)) AS v (TaxType, CardCode, DueDay)
                INNER JOIN BUSINESS_PARTNERS bp ON bp.CardCode = v.CardCode
                WHERE NOT EXISTS (SELECT 1 FROM WITHHOLDING_TAX_SETTINGS s WHERE s.TaxType = v.TaxType);
                """);
        }

        /// <summary>Sem volta: apagar os parceiros poderia atingir cadastros que o usuário já completou.</summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
