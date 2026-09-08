using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext;

/// <summary>
/// Semeia a numeração do documento financeiro (TransactionCode 12).
/// Idempotente de propósito: o índice único (TransactionCode, Name) faria um INSERT cego
/// derrubar o deploy em qualquer base onde a numeração já tenha sido criada pela tela.
/// </summary>
public partial class SeedFinancialDocumentDocNumber : Migration
{
    private const string SeedKey = "3F6C1B84-9A27-4D50-8E13-7C2A5B4E9D60";

    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql($"""
            IF NOT EXISTS (SELECT 1 FROM DOC_NUMBERS WHERE TransactionCode = 12)
            INSERT INTO DOC_NUMBERS ([Key], TransactionCode, Name, FirstNumber, LastNumber,
                                     NextNumber, [Default], Prefix, Suffix, BranchCode,
                                     Inactive, IsManual, NumberSize)
            VALUES ('{SeedKey}', 12, 'FINANCEIRO', 1, 0, 1, 1, 'FN', '', NULL, 0, 0, '6');
            """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql($"DELETE FROM DOC_NUMBERS WHERE [Key] = '{SeedKey}';");
}
