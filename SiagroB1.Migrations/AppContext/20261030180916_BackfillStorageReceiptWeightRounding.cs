using Microsoft.EntityFrameworkCore.Migrations;
using SiagroB1.Domain.Enums;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <summary>
    /// GAC-1205: os romaneios de ENTRADA EM ARMAZENAGEM confirmados antes da correção ficaram com
    /// descontos e peso líquido com casas decimais. Aplica a mesma regra de
    /// <c>WeightRounding.ToKilograms</c> (kg inteiro, Round Half Up): cada desconto arredondado e o
    /// líquido = bruto − descontos arredondados, arredondado. O <c>ROUND(x, 0)</c> do SQL Server
    /// arredonda o meio para longe do zero, igual ao <c>MidpointRounding.AwayFromZero</c>.
    /// <para>
    /// Só o tipo <see cref="StorageTransactionType.Receipt"/>, por decisão do usuário (30/10/2026):
    /// compras e vendas da Expedição estão amarradas a contratos, NF-e já transmitidas e títulos.
    /// O valor dos serviços não muda, porque é calculado pelo peso bruto. Faturado também entra,
    /// pelo mesmo motivo.
    /// </para>
    /// <para>
    /// ⚠️ O saldo diário (<c>STORAGE_DAILY_BALANCES</c>) é derivado e NÃO é recalculado aqui: depois
    /// de aplicar, reprocessar por lote os lotes afetados desde a data do primeiro romaneio alterado.
    /// </para>
    /// </summary>
    /// <inheritdoc />
    public partial class BackfillStorageReceiptWeightRounding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
UPDATE STORAGE_TRANSACTIONS
SET CleaningDiscount = ROUND(CleaningDiscount, 0),
    DryingDiscount   = ROUND(DryingDiscount, 0),
    OthersDicount    = ROUND(OthersDicount, 0),
    NetWeight        = ROUND(GrossWeight - (ROUND(CleaningDiscount, 0) + ROUND(DryingDiscount, 0) + ROUND(OthersDicount, 0)), 0),
    AvaiableVolumeToAllocate = CASE WHEN AvaiableVolumeToAllocate = NetWeight
        THEN ROUND(GrossWeight - (ROUND(CleaningDiscount, 0) + ROUND(DryingDiscount, 0) + ROUND(OthersDicount, 0)), 0)
        ELSE AvaiableVolumeToAllocate END
WHERE TransactionType = {(int)StorageTransactionType.Receipt}
  AND TransactionStatus IN ({(int)StorageTransactionsStatus.Confirmed}, {(int)StorageTransactionsStatus.Invoiced})
  AND (CleaningDiscount <> ROUND(CleaningDiscount, 0)
    OR DryingDiscount   <> ROUND(DryingDiscount, 0)
    OR OthersDicount    <> ROUND(OthersDicount, 0)
    OR NetWeight        <> ROUND(GrossWeight - (ROUND(CleaningDiscount, 0) + ROUND(DryingDiscount, 0) + ROUND(OthersDicount, 0)), 0));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op deliberado: os decimais descartados não ficam guardados em lugar nenhum.
        }
    }
}
