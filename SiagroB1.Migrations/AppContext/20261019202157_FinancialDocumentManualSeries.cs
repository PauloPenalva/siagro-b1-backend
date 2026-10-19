using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <summary>
    /// Inclusão manual de títulos em Contas a Pagar/Receber: o Financeiro (TransactionCode 12) passa a admitir a série
    /// "Manual" (código digitado), ao lado da padrão "Automático". Só dados e idempotente, no molde da
    /// DocNumberManualSeries: cria a Manual só onde o tipo já tem série padrão e ainda não tem manual nem uma série
    /// chamada "Manual".
    /// </summary>
    public partial class FinancialDocumentManualSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO DOC_NUMBERS ([Key], TransactionCode, Name, FirstNumber, LastNumber,
                                         NextNumber, [Default], Prefix, Suffix, BranchCode,
                                         Inactive, IsManual, NumberSize)
                SELECT NEWID(), 12, 'Manual', 0, 0, 0, 0, '', '', NULL, 0, 1, NULL
                WHERE EXISTS (SELECT 1 FROM DOC_NUMBERS d
                              WHERE d.TransactionCode = 12 AND d.[Default] = 1)
                  AND NOT EXISTS (SELECT 1 FROM DOC_NUMBERS d
                                  WHERE d.TransactionCode = 12
                                    AND (d.IsManual = 1 OR d.Name = 'Manual'));

                IF NOT EXISTS (SELECT 1 FROM DOC_NUMBERS WHERE TransactionCode = 12 AND IsManual = 1)
                    PRINT N'FinancialDocumentManualSeries: série Manual do Financeiro não criada (sem série padrão ou já existe uma série "Manual" automática).';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Só apaga a Manual se nenhum título usa a série.
            migrationBuilder.Sql("""
                DELETE d FROM DOC_NUMBERS d
                WHERE d.TransactionCode = 12 AND d.IsManual = 1 AND d.Name = 'Manual'
                  AND NOT EXISTS (SELECT 1 FROM FINANCIAL_DOCUMENTS f WHERE f.DocNumberKey = d.[Key]);
                """);
        }
    }
}
