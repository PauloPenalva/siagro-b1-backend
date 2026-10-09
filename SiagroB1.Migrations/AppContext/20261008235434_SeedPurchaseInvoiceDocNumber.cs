using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <summary>
    /// Numeração padrão do Documento de Entrada (TransactionCode = 14, prefixo DE + 6 dígitos, o mesmo
    /// formato do <c>DocNumberSequenceService</c>: prefixo + número com zeros à esquerda + sufixo).
    /// As entradas que já existem sem número interno são numeradas aqui, na ordem de criação; número
    /// digitado à mão é preservado. A sequência segue do último número atribuído. Só dados: o modelo
    /// não muda.
    /// </summary>
    public partial class SeedPurchaseInvoiceDocNumber : Migration
    {
        private const string SeedKey = "8B2D4F61-3A97-4C0E-B5D8-1E7A9C3F2B54";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                IF NOT EXISTS (SELECT 1 FROM DOC_NUMBERS WHERE TransactionCode = 14)
                INSERT INTO DOC_NUMBERS ([Key], TransactionCode, Name, FirstNumber, LastNumber,
                                         NextNumber, [Default], Prefix, Suffix, BranchCode,
                                         Inactive, IsManual, NumberSize)
                VALUES ('{SeedKey}', 14, 'DOCUMENTO DE ENTRADA', 1, 0, 1, 1, 'DE', '', NULL, 0, 0, '6');
                """);

            // Backfill: só quem está sem número; a ordem é a de criação (Key desempata).
            migrationBuilder.Sql($"""
                ;WITH Numbered AS (
                    SELECT [Key], ROW_NUMBER() OVER (ORDER BY CreatedAt, [Key]) AS Seq
                    FROM PURCHASE_INVOICES
                    WHERE InvoiceNumber IS NULL OR InvoiceNumber = ''
                )
                UPDATE pi
                SET InvoiceNumber = 'DE' + RIGHT(REPLICATE('0', 6) + CAST(n.Seq AS VARCHAR(10)),
                                                 CASE WHEN LEN(CAST(n.Seq AS VARCHAR(10))) > 6
                                                      THEN LEN(CAST(n.Seq AS VARCHAR(10))) ELSE 6 END),
                    DocNumberKey = '{SeedKey}'
                FROM PURCHASE_INVOICES pi
                JOIN Numbered n ON n.[Key] = pi.[Key];
                """);

            // O serviço faz LastNumber = NextNumber e NextNumber + 1: o próximo número é o seguinte ao último atribuído.
            migrationBuilder.Sql($"""
                UPDATE DOC_NUMBERS
                SET LastNumber = (SELECT COUNT(*) FROM PURCHASE_INVOICES WHERE DocNumberKey = '{SeedKey}'),
                    NextNumber = (SELECT COUNT(*) FROM PURCHASE_INVOICES WHERE DocNumberKey = '{SeedKey}') + 1
                WHERE [Key] = '{SeedKey}';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                UPDATE PURCHASE_INVOICES
                SET InvoiceNumber = NULL, DocNumberKey = NULL
                WHERE DocNumberKey = '{SeedKey}' AND InvoiceNumber LIKE 'DE%';
                """);

            migrationBuilder.Sql($"DELETE FROM DOC_NUMBERS WHERE [Key] = '{SeedKey}';");
        }
    }
}
