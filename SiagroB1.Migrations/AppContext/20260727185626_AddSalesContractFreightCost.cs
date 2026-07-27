using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <summary>
    /// Custo do frete negociado no contrato de VENDA, espelhando o contrato de compra:
    /// <c>FreightCostStandard</c> (valor por unidade) e <c>FreightUmCode</c> (unidade de medida
    /// do frete). Até aqui a venda só registrava o <c>FreightTerms</c> (CIF/FOB/sem frete), sem
    /// valor — o que impedia a apuração de resultado do contrato e deixava a coluna Frete do
    /// relatório de contratos por produto sem o número.
    ///
    /// <c>FreightUmCode</c> nasce SEM foreign key para UNITS_OF_MEASURE, de propósito: em modo
    /// SAPB1 a unidade de medida vem do SAP e a tabela local fica vazia, então a FK zeraria a
    /// coleção. É o mesmo tratamento de <c>UnitOfMeasureCode</c> em SALES_CONTRACTS e o estado
    /// atual de PURCHASE_CONTRACTS, cuja FK original foi removida do modelo.
    ///
    /// O valor também alimenta <c>SALES_CONTRACTS_PRICE_FIXATIONS.FreightCost</c> na fixação
    /// automática dos contratos de preço fixo (FIX).
    /// </summary>
    public partial class AddSalesContractFreightCost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "FreightCostStandard",
                table: "SALES_CONTRACTS",
                type: "DECIMAL(18,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "FreightUmCode",
                table: "SALES_CONTRACTS",
                type: "VARCHAR(4)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FreightCostStandard",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "FreightUmCode",
                table: "SALES_CONTRACTS");
        }
    }
}
