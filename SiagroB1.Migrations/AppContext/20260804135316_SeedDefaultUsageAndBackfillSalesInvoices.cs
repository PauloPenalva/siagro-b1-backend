using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <summary>
    /// Natureza de operação semente + backfill dos documentos de saída existentes.
    ///
    /// Todo documento já gravado nasceu de faturamento de romaneio e não tem natureza.
    /// Deixar UsageCode nulo e marcar o campo como obrigatório na tela travaria a edição de
    /// TODO registro legado — armadilha já vivida nesta base (campo obrigatório sobre coluna
    /// nova sem backfill). Por isso a semente é criada e aplicada a todos eles.
    ///
    /// Efeitos da semente: ContractBalanceEffect = Consume (1), ContractValueEffect = None (0).
    /// O PriceDifference gravado pelo faturamento de romaneio é APURAÇÃO da diferença entre o
    /// preço da nota e o do contrato, não liquidação — e quem o grava é o caminho de
    /// faturamento, que decide pela ORIGEM do documento (tem romaneio ou não) e ignora os
    /// efeitos da natureza.
    ///
    /// Idempotente: reaplicar não duplica a semente nem re-preenche quem já tem natureza.
    /// </summary>
    public partial class SeedDefaultUsageAndBackfillSalesInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM USAGES WHERE Name = 'Venda de grãos')
                BEGIN
                    INSERT INTO USAGES
                        (Name, Description, CfopOutgoingInState, CfopOutgoingOutState,
                         ContractBalanceEffect, ContractValueEffect,
                         RequiresContract, RequiresQuantity, RequiresWeight, Inactive)
                    VALUES
                        ('Venda de grãos', 'Natureza padrão do faturamento de romaneio',
                         '5102', '6102', 1, 0, 1, 1, 1, 0);
                END;

                UPDATE SALES_INVOICES
                   SET UsageCode = (SELECT TOP 1 Code FROM USAGES WHERE Name = 'Venda de grãos')
                 WHERE UsageCode IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE SALES_INVOICES
                   SET UsageCode = NULL
                 WHERE UsageCode = (SELECT TOP 1 Code FROM USAGES WHERE Name = 'Venda de grãos');

                DELETE FROM USAGES WHERE Name = 'Venda de grãos';
            ");
        }
    }
}
