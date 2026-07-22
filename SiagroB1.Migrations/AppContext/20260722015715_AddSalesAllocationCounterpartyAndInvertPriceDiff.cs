using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddSalesAllocationCounterpartyAndInvertPriceDiff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CounterpartySalesContractKey",
                table: "SALES_CONTRACTS_ALLOCATIONS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SALES_CONTRACTS_ALLOCATIONS_CounterpartySalesContractKey",
                table: "SALES_CONTRACTS_ALLOCATIONS",
                column: "CounterpartySalesContractKey");

            // Inversão do sinal da diferença de preço: antes era Volume × (ContractPrice −
            // InvoiceUnitPrice); agora Volume × (InvoiceUnitPrice − ContractPrice) — NF maior
            // que o contrato fica positivo (sobra). Nega os valores já gravados.
            migrationBuilder.Sql(
                "UPDATE SALES_CONTRACTS_ALLOCATIONS SET PriceDifference = -PriceDifference;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restaura o sinal anterior da diferença de preço.
            migrationBuilder.Sql(
                "UPDATE SALES_CONTRACTS_ALLOCATIONS SET PriceDifference = -PriceDifference;");

            migrationBuilder.DropIndex(
                name: "IX_SALES_CONTRACTS_ALLOCATIONS_CounterpartySalesContractKey",
                table: "SALES_CONTRACTS_ALLOCATIONS");

            migrationBuilder.DropColumn(
                name: "CounterpartySalesContractKey",
                table: "SALES_CONTRACTS_ALLOCATIONS");
        }
    }
}
