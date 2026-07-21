using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <summary>
    /// Promove a Confirmed as fixações automáticas de contratos de preço fixo.
    ///
    /// Essas fixações espelham o preço já acordado na negociação e nasciam InApproval,
    /// numa época em que TotalPrice contava InApproval e Confirmed indistintamente.
    /// Com TotalPrice passando a contar apenas Confirmed, elas zerariam o valor e o
    /// imposto de TODO contrato de preço fixo existente, além de entulhar a fila de
    /// aprovação da diretoria com itens que ninguém deve aprovar.
    ///
    /// Escopo restrito a Type = 0 (Fixed). Fixações de contrato a fixar (PAF) ficam
    /// intactas — lá InApproval é um estado legítimo, aguardando a diretoria.
    /// </summary>
    /// <inheritdoc />
    public partial class ConfirmFixedContractAutoFixations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Status: 0 = InApproval, 1 = Confirmed. Type: 0 = Fixed, 1 = ToBeDetermined.
            migrationBuilder.Sql(@"
                UPDATE pf
                   SET pf.Status = 1
                  FROM PURCHASE_CONTRACTS_PRICE_FIXATIONS pf
                  INNER JOIN PURCHASE_CONTRACTS pc ON pc.[Key] = pf.PurchaseContractKey
                 WHERE pf.Status = 0
                   AND pc.Type = 0;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverte apenas o que o Up promoveu: fixações de contrato fixo.
            // Não distingue as que já eram Confirmed antes desta migration — aceitável
            // porque, antes dela, nenhuma fixação de contrato fixo era Confirmed.
            migrationBuilder.Sql(@"
                UPDATE pf
                   SET pf.Status = 0
                  FROM PURCHASE_CONTRACTS_PRICE_FIXATIONS pf
                  INNER JOIN PURCHASE_CONTRACTS pc ON pc.[Key] = pf.PurchaseContractKey
                 WHERE pf.Status = 1
                   AND pc.Type = 0;
            ");
        }
    }
}
