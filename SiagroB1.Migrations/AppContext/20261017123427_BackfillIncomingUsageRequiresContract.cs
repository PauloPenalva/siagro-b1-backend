using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class BackfillIncomingUsageRequiresContract : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Spec 2026-10-17 §1: a natureza de entrada passa a decidir se a linha exige contrato (Direction 2 = Incoming).
            // Só dado, sem mudar o esquema. Sem este backfill, naturezas de entrada de hoje virariam "outra compra" ou
            // passariam a recusar o contrato que as linhas de grão já carregam.
            //
            // a) Natureza de entrada SEM linha em USAGE_EFFECTS lê RequiresContract = false; ganha a linha com
            //    RequiresContract = 0 (ruling R8: quem decide é o passo b) e os demais campos no padrão da entidade
            //    (RequiresQuantity = 1, o resto 0).
            migrationBuilder.Sql(@"
INSERT INTO USAGE_EFFECTS (UsageCode, ContractBalanceEffect, ContractValueEffect, IsDefault, RequiresContract,
                           RequiresQuantity, RequiresWeight, StorageCustodyRole, StorageDepositRole)
SELECT u.Code, 0, 0, 0, 0, 1, 0, 0, 0
FROM USAGES u
WHERE u.Direction = 2
  AND NOT EXISTS (SELECT 1 FROM USAGE_EFFECTS e WHERE e.UsageCode = u.Code);");

            // b) Natureza de entrada que gera financeiro OU que já é usada em linha com contrato passa a exigir contrato.
            migrationBuilder.Sql(@"
UPDATE e SET RequiresContract = 1
FROM USAGE_EFFECTS e
JOIN USAGES u ON u.Code = e.UsageCode
WHERE u.Direction = 2
  AND (u.CreatesFinancialDocument = 1
       OR EXISTS (SELECT 1 FROM PURCHASE_INVOICES_ITEMS i
                  WHERE i.UsageCode = u.Code AND i.PurchaseContractKey IS NOT NULL));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
