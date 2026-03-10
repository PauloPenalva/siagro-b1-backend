using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.StorageAddresses;

public class StorageAddressesStorageChargeCalculatorService(IUnitOfWork db)
{
    public async Task CalculateAsync(DateTime processingDate, CancellationToken ct = default)
    {
        var addresses = await db.Context.StorageAddresses
            .Include(x => x.ProcessingCost)
            .Where(x => x.Status == StorageAddressStatus.Open && x.ProcessingCostCode != null)
            .ToListAsync(ct);

        foreach (var address in addresses)
        {
            var cost = address.ProcessingCost;
            if (cost == null || cost.StoragePrice is null || cost.StoragePrice <= 0)
                continue;

            var firstReceiptDate = await db.Context.StorageTransactions
                .Where(x =>
                    x.StorageAddressCode == address.Code &&
                    x.TransactionType == StorageTransactionType.Receipt &&
                    (x.TransactionStatus == StorageTransactionsStatus.Confirmed ||
                     x.TransactionStatus == StorageTransactionsStatus.Invoiced))
                .MinAsync(x => (DateTime?)x.TransactionDate, ct);

            if (!firstReceiptDate.HasValue)
                continue;

            var graceDays = cost.StorageGraceDays ?? 0;
            var intervalDays = cost.StorageBillingIntervalDays ?? 0;

            if (intervalDays <= 0)
                continue;

            var billableStartDate = firstReceiptDate.Value.Date.AddDays(graceDays);

            if (processingDate.Date < billableStartDate)
                continue;

            var elapsedBillableDays = (processingDate.Date - billableStartDate).Days + 1;
            if (elapsedBillableDays % intervalDays != 0)
                continue;

            var periodEnd = processingDate.Date;
            var periodStart = periodEnd.AddDays(-(intervalDays - 1));

            var alreadyExists = await db.Context.StorageCharges.AnyAsync(x =>
                x.StorageAddressCode == address.Code &&
                x.ChargeType == StorageChargeType.Storage &&
                x.PeriodStart == periodStart &&
                x.PeriodEnd == periodEnd, ct);
            
            if (alreadyExists)
                continue;
            
            var balances = await db.Context.StorageDailyBalances
                .Where(x =>
                    x.StorageAddressCode == address.Code &&
                    x.BalanceDate >= periodStart &&
                    x.BalanceDate <= periodEnd)
                .ToListAsync(ct);

            if (balances.Count == 0)
                continue;

            var tonDays = balances.Sum(x => x.BillableBalance);
            var amount = tonDays * cost.StoragePrice.Value;
            
            var charge = new StorageCharge
            {
                StorageAddressCode = address.Code,
                ChargeType = StorageChargeType.Storage,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                BaseQuantity = balances.Last().ClosingBalance,
                TonDays = tonDays,
                UnitPriceOrRate = cost.StoragePrice.Value,
                TotalAmount = Math.Round(amount, 2, MidpointRounding.AwayFromZero),
                TotalQuantityLoss = 0m,
                ProcessingCostCode = cost.Code,
                CalculationDate = DateTime.Now,
                Notes = "Cobrança automática de armazenagem."
            };

            db.Context.StorageCharges.Add(charge);
        }

        await db.Context.SaveChangesAsync(ct);
    }
}