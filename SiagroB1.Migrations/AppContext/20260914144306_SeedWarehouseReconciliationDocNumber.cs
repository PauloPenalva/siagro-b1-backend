using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <summary>
    /// Numeração padrão da Conferência de Saldo de Armazém (TransactionCode = 13). Mesmo motivo de
    /// <c>SeedShipmentLoadDocNumber</c>: sem linha <c>[Default] = 1</c> o
    /// <c>DocNumberSequenceService</c> lança e a primeira conferência falha. SQL idempotente e GUID
    /// fixo para o Down remover só esta linha.
    /// </summary>
    public partial class SeedWarehouseReconciliationDocNumber : Migration
    {
        private const string SeedKey = "3F6A2B91-7C4D-4E58-A1B2-9D0E8C7F6A13";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                IF NOT EXISTS (SELECT 1 FROM DOC_NUMBERS WHERE TransactionCode = 13)
                INSERT INTO DOC_NUMBERS ([Key], TransactionCode, Name, FirstNumber, LastNumber,
                                         NextNumber, [Default], Prefix, Suffix, BranchCode,
                                         Inactive, IsManual, NumberSize)
                VALUES ('{SeedKey}', 13, 'CONFERENCIA SALDO', 1, 0, 1, 1, 'CS', '', NULL, 0, 0, '6');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DELETE FROM DOC_NUMBERS WHERE [Key] = '{SeedKey}';");
        }
    }
}
