using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddFreightDocNumbers : Migration
    {
        private const string OrderKey = "5D1A7E42-9C36-4B80-A2F7-6E3B1C8D0F95";
        private const string InvoiceKey = "C47B2E90-1D58-4A63-8F0E-93A5B6D2E417";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Séries padrão "Automático" da Ordem de Frete (19) e da Fatura de Frete (20), no molde do
            // SeedPurchaseInvoiceDocNumber: sem elas a inclusão falha por falta de série.
            migrationBuilder.Sql($"""
                IF NOT EXISTS (SELECT 1 FROM DOC_NUMBERS WHERE TransactionCode = 19)
                INSERT INTO DOC_NUMBERS ([Key], TransactionCode, Name, FirstNumber, LastNumber,
                                         NextNumber, [Default], Prefix, Suffix, BranchCode,
                                         Inactive, IsManual, NumberSize)
                VALUES ('{OrderKey}', 19, 'Automático', 1, 0, 1, 1, 'OF', '', NULL, 0, 0, '6');

                IF NOT EXISTS (SELECT 1 FROM DOC_NUMBERS WHERE TransactionCode = 20)
                INSERT INTO DOC_NUMBERS ([Key], TransactionCode, Name, FirstNumber, LastNumber,
                                         NextNumber, [Default], Prefix, Suffix, BranchCode,
                                         Inactive, IsManual, NumberSize)
                VALUES ('{InvoiceKey}', 20, 'Automático', 1, 0, 1, 1, 'FF', '', NULL, 0, 0, '6');
                """);

            migrationBuilder.AddColumn<string>(
                name: "EndMunicipalityName",
                table: "FREIGHT_ORDERS",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipientName",
                table: "FREIGHT_ORDERS",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SenderName",
                table: "FREIGHT_ORDERS",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StartMunicipalityName",
                table: "FREIGHT_ORDERS",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TakerName",
                table: "FREIGHT_ORDERS",
                type: "VARCHAR(100)",
                nullable: true);

            // Ordens já gravadas: os nomes dos municípios saem do cadastro.
            migrationBuilder.Sql("""
                UPDATE o SET StartMunicipalityName = m.Name
                FROM FREIGHT_ORDERS o JOIN MUNICIPALITIES m ON m.Code = o.StartMunicipalityCode
                WHERE o.StartMunicipalityName IS NULL;
                UPDATE o SET EndMunicipalityName = m.Name
                FROM FREIGHT_ORDERS o JOIN MUNICIPALITIES m ON m.Code = o.EndMunicipalityCode
                WHERE o.EndMunicipalityName IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Só apaga a série se nenhuma ordem a usa.
            migrationBuilder.Sql($"""
                DELETE FROM DOC_NUMBERS WHERE [Key] = '{OrderKey}'
                  AND NOT EXISTS (SELECT 1 FROM FREIGHT_ORDERS WHERE DocNumberKey = '{OrderKey}');
                DELETE FROM DOC_NUMBERS WHERE [Key] = '{InvoiceKey}';
                """);

            migrationBuilder.DropColumn(
                name: "EndMunicipalityName",
                table: "FREIGHT_ORDERS");

            migrationBuilder.DropColumn(
                name: "RecipientName",
                table: "FREIGHT_ORDERS");

            migrationBuilder.DropColumn(
                name: "SenderName",
                table: "FREIGHT_ORDERS");

            migrationBuilder.DropColumn(
                name: "StartMunicipalityName",
                table: "FREIGHT_ORDERS");

            migrationBuilder.DropColumn(
                name: "TakerName",
                table: "FREIGHT_ORDERS");
        }
    }
}
