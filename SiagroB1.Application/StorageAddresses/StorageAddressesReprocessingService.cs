using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.StorageAddresses;

public class StorageAddressesReprocessingService(
    IUnitOfWork db,
    StorageAddressesCalculationOrchestratorService orchestrator
    )
{
    public async Task ReprocessAsync(string storageAddressCode, DateTime fromDate, DateTime toDate, CancellationToken ct = default)
    {
        var balances = db.Context.StorageDailyBalances
            .Where(x => x.StorageAddressCode == storageAddressCode && x.BalanceDate >= fromDate.Date);

        db.Context.StorageDailyBalances.RemoveRange(balances);

        var charges = db.Context.StorageCharges
            .Where(x => x.StorageAddressCode == storageAddressCode && x.PeriodEnd >= fromDate.Date);

        db.Context.StorageCharges.RemoveRange(charges);

        var autoLossTransactions = db.Context.StorageTransactions
            .Where(x =>
                x.StorageAddressCode == storageAddressCode &&
                x.TransactionType == StorageTransactionType.TechnicalLoss &&
                x.Comments != null &&
                x.Comments.StartsWith("QUEBRA_TECNICA_") &&
                x.TransactionDate >= fromDate.Date);

        db.Context.StorageTransactions.RemoveRange(autoLossTransactions);

        await db.SaveChangesAsync();

        for (var date = fromDate.Date; date <= toDate.Date; date = date.AddDays(1))
        {
            await orchestrator.ExecuteAsync(date, ct);
        }
    }
}