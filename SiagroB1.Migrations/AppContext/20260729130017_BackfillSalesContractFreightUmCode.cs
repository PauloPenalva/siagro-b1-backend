using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <summary>
    /// Preenche com 'TON' a UM do frete dos contratos de venda anteriores à criação da coluna.
    ///
    /// A coluna SALES_CONTRACTS.FreightUmCode nasceu anulável em AddSalesContractFreightCost
    /// (27/07/2026) e sem backfill, então todo contrato criado antes dessa data ficou NULL.
    /// Na tela o campo é readonly (preenchido só na inclusão, a partir de
    /// SYSTEM_SETUP.DefaultFreightUoM) e ao mesmo tempo obrigatório — a validação do formulário
    /// não isenta campos não editáveis. Resultado: abrir um contrato antigo em edição travava o
    /// salvamento num campo que o usuário não tinha como preencher.
    ///
    /// Escopo restrito aos contratos de VENDA. PURCHASE_CONTRACTS.FreightUmCode tem o mesmo
    /// padrão, mas ficou fora desta entrega por decisão de escopo.
    /// </summary>
    public partial class BackfillSalesContractFreightUmCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotente: o WHERE limita a linhas ainda não preenchidas, então reexecutar é
            // inofensivo. Preserva os contratos criados a partir de 27/07/2026, que já gravaram
            // o valor vindo do SYSTEM_SETUP e podem legitimamente não ser 'TON'.
            migrationBuilder.Sql(@"
                UPDATE SALES_CONTRACTS
                   SET FreightUmCode = 'TON'
                 WHERE FreightUmCode IS NULL
                    OR LTRIM(RTRIM(FreightUmCode)) = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sem reversão. Depois do Up não há como distinguir as linhas que ele preencheu
            // daquelas que já valiam 'TON' por configuração — um UPDATE ... SET FreightUmCode =
            // NULL WHERE FreightUmCode = 'TON' apagaria dado bom. Como o estado anterior era
            // justamente a ausência de valor, deixar nulo de novo não tem utilidade prática.
        }
    }
}
