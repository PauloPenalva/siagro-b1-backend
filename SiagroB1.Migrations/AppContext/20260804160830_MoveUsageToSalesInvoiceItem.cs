using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <summary>
    /// A natureza de operação passa a ser de LINHA, como no SAP: cada item tem a sua, resolve
    /// o próprio CFOP e produz o próprio efeito no contrato. O nome vai desnormalizado junto,
    /// como <c>ItemName</c>, para a grade não depender do cadastro (que em SAPB1 nem é local).
    ///
    /// A ORDEM importa. O EF gerou o DROP do cabeçalho ANTES do ADD da linha, o que jogaria
    /// fora o backfill feito na migration da semente. Aqui é: cria as colunas na linha, COPIA
    /// do cabeçalho, e só então dropa o cabeçalho.
    /// </summary>
    public partial class MoveUsageToSalesInvoiceItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UsageCode",
                table: "SALES_INVOICES_ITEMS",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UsageName",
                table: "SALES_INVOICES_ITEMS",
                type: "VARCHAR(200)",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE i
                   SET i.UsageCode = d.UsageCode
                  FROM SALES_INVOICES_ITEMS i
                  JOIN SALES_INVOICES d ON d.[Key] = i.SalesInvoiceKey
                 WHERE i.UsageCode IS NULL
                   AND d.UsageCode IS NOT NULL;

                UPDATE i
                   SET i.UsageName = u.Name
                  FROM SALES_INVOICES_ITEMS i
                  JOIN USAGES u ON u.Code = i.UsageCode
                 WHERE i.UsageName IS NULL;
            ");

            migrationBuilder.DropColumn(
                name: "UsageCode",
                table: "SALES_INVOICES");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UsageCode",
                table: "SALES_INVOICES",
                type: "int",
                nullable: true);

            // Volta a natureza para o cabeçalho pegando a da PRIMEIRA linha: o desenho de
            // linha permite naturezas diferentes no mesmo documento e o de cabeçalho não, então
            // reverter é necessariamente com perda quando houver mistura.
            migrationBuilder.Sql(@"
                UPDATE d
                   SET d.UsageCode = x.UsageCode
                  FROM SALES_INVOICES d
                  CROSS APPLY (
                        SELECT TOP 1 i.UsageCode
                          FROM SALES_INVOICES_ITEMS i
                         WHERE i.SalesInvoiceKey = d.[Key]
                           AND i.UsageCode IS NOT NULL
                       ) x;
            ");

            migrationBuilder.DropColumn(
                name: "UsageCode",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "UsageName",
                table: "SALES_INVOICES_ITEMS");
        }
    }
}
