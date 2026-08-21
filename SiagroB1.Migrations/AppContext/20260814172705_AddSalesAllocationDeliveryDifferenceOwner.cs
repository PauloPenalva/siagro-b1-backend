using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddSalesAllocationDeliveryDifferenceOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "OwnsDeliveryDifference",
                table: "SALES_CONTRACTS_ALLOCATIONS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Backfill: elege o dono da diferença de entrega de CADA item já lançado no
            // ledger, aplicando em SQL a mesma regra de
            // SalesContractsDeliveryDifferenceOwnerService — a linha mais antiga (RowId)
            // entre as que estão em contrato com líquido positivo no item; se nenhum contrato
            // tem líquido positivo (item integralmente devolvido), a mais antiga do item.
            //
            // Precisa rodar ANTES do índice único filtrado: sem ele não haveria o que
            // indexar, e um eventual empate (dois donos no mesmo item) tem que estourar na
            // criação do índice, em vez de passar despercebido em produção.
            migrationBuilder.Sql("""
                WITH ContractNet AS (
                    SELECT SalesInvoiceItemKey, SalesContractKey, SUM(Volume) AS Net
                      FROM SALES_CONTRACTS_ALLOCATIONS
                     GROUP BY SalesInvoiceItemKey, SalesContractKey
                ),
                Candidate AS (
                    SELECT a.[Key],
                           ROW_NUMBER() OVER (
                               PARTITION BY a.SalesInvoiceItemKey
                               ORDER BY CASE WHEN n.Net > 0 THEN 0 ELSE 1 END, a.RowId
                           ) AS Priority
                      FROM SALES_CONTRACTS_ALLOCATIONS a
                     INNER JOIN ContractNet n
                             ON n.SalesInvoiceItemKey = a.SalesInvoiceItemKey
                            AND n.SalesContractKey = a.SalesContractKey
                )
                UPDATE a
                   SET a.OwnsDeliveryDifference = 1
                  FROM SALES_CONTRACTS_ALLOCATIONS a
                 INNER JOIN Candidate c ON c.[Key] = a.[Key]
                 WHERE c.Priority = 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_SALES_CONTRACTS_ALLOCATIONS_DeliveryDifferenceOwner",
                table: "SALES_CONTRACTS_ALLOCATIONS",
                column: "SalesInvoiceItemKey",
                unique: true,
                filter: "[OwnsDeliveryDifference] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SALES_CONTRACTS_ALLOCATIONS_DeliveryDifferenceOwner",
                table: "SALES_CONTRACTS_ALLOCATIONS");

            migrationBuilder.DropColumn(
                name: "OwnsDeliveryDifference",
                table: "SALES_CONTRACTS_ALLOCATIONS");
        }
    }
}
