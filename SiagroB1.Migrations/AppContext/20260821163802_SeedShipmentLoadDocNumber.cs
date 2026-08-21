using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <summary>
    /// Numeração padrão da Carga (TransactionCode = 11).
    /// </summary>
    /// <remarks>
    /// Não há precedente de seed de DOC_NUMBERS em migration — a tabela é mantida pelo usuário
    /// na tela /doc-numbers. Esta é a exceção necessária: <c>DocNumberSequenceService</c> exige
    /// uma linha com <c>[Default] = 1</c> e lança <c>NotFoundException</c> sem ela, então a
    /// primeira carga falharia no dia 1.
    /// <para>
    /// SQL bruto idempotente, e não <c>InsertData</c>: se o usuário já tiver criado a numeração
    /// à mão, o INSERT colidiria com o índice único (TransactionCode, Name) e derrubaria o deploy.
    /// GUID fixo para o Down conseguir remover exatamente a linha que este Up criou.
    /// </para>
    /// </remarks>
    public partial class SeedShipmentLoadDocNumber : Migration
    {
        private const string SeedKey = "8B1D2C6E-0F47-4A93-9C21-5E7A4B60D3F1";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                IF NOT EXISTS (SELECT 1 FROM DOC_NUMBERS WHERE TransactionCode = 11)
                INSERT INTO DOC_NUMBERS ([Key], TransactionCode, Name, FirstNumber, LastNumber,
                                         NextNumber, [Default], Prefix, Suffix, BranchCode,
                                         Inactive, IsManual, NumberSize)
                VALUES ('{SeedKey}', 11, 'CARGA', 1, 0, 1, 1, 'CG', '', NULL, 0, 0, '6');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Só a linha semeada aqui. Numeração que o usuário tenha criado à mão fica intacta.
            migrationBuilder.Sql($"DELETE FROM DOC_NUMBERS WHERE [Key] = '{SeedKey}';");
        }
    }
}
