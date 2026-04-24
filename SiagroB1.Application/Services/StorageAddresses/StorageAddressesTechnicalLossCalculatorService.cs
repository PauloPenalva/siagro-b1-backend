using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.StorageAddresses;

public class StorageAddressesTechnicalLossCalculatorService(
    IUnitOfWork db,
    DocNumberSequenceService numberSequenceService
    )
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
            if (cost == null || cost.TechnicalLossRate is null || cost.TechnicalLossRate <= 0)
                continue;

            var receiptDate = cost.StoragePriceCalculationMethod switch
            {
                StoragePriceCalculationMethod.AfterFirstReceipting => await db.Context.StorageTransactions.Where(x =>
                        x.StorageAddressCode == address.Code && x.TransactionType == StorageTransactionType.Receipt &&
                        (x.TransactionStatus == StorageTransactionsStatus.Confirmed ||
                         x.TransactionStatus == StorageTransactionsStatus.Invoiced))
                    .MinAsync(x => (DateTime?)x.TransactionDate, ct),
                
                StoragePriceCalculationMethod.AfterLastReceipting => await db.Context.StorageTransactions.Where(x =>
                        x.StorageAddressCode == address.Code && x.TransactionType == StorageTransactionType.Receipt &&
                        (x.TransactionStatus == StorageTransactionsStatus.Confirmed ||
                         x.TransactionStatus == StorageTransactionsStatus.Invoiced))
                    .MaxAsync(x => (DateTime?)x.TransactionDate, ct),
                _ => null
            };

            if (!receiptDate.HasValue)
                continue;

            var graceDays = cost.TechnicalLossGraceDays ?? 0;
            var intervalDays = cost.TechnicalLossIntervalDays ?? 0;

            if (intervalDays <= 0)
                continue;

            var billableStartDate = receiptDate.Value.Date.AddDays(graceDays);

            if (processingDate.Date < billableStartDate)
                continue;

            var elapsedBillableDays = (processingDate.Date - billableStartDate).Days + 1;
            if (elapsedBillableDays % intervalDays != 0)
                continue;

            var periodEnd = processingDate.Date;
            var periodStart = periodEnd.AddDays(-(intervalDays - 1));

            var alreadyExists = await db.Context.StorageCharges.AnyAsync(x =>
                x.StorageAddressCode == address.Code &&
                x.ChargeType == StorageChargeType.TechnicalLoss &&
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

            var ratePerDay = cost.TechnicalLossRate.Value / intervalDays;
            var quantityLoss = Math.Round(tonDays * ratePerDay, 3, MidpointRounding.AwayFromZero);

            if (quantityLoss <= 0)
                continue;

            var charge = new StorageCharge
            {
                StorageAddressCode = address.Code,
                ChargeType = StorageChargeType.TechnicalLoss,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                BaseQuantity = balances.Last().ClosingBalance,
                TonDays = tonDays,
                UnitPriceOrRate = cost.TechnicalLossRate.Value,
                TotalAmount = 0m,
                TotalQuantityLoss = quantityLoss,
                ProcessingCostCode = cost.Code,
                CalculationDate = DateTime.Now,
                Notes = "Apropriação automática de quebra técnica."
            };

            db.Context.StorageCharges.Add(charge);

            var txExists = await db.Context.StorageTransactions.AnyAsync(x =>
                x.StorageAddressCode == address.Code &&
                x.TransactionType == StorageTransactionType.TechnicalLoss &&
                x.TransactionDate == processingDate.Date &&
                x.Comments == $"QUEBRA_TECNICA_{periodStart:yyyyMMdd}_{periodEnd:yyyyMMdd}", ct);

            if (!txExists)
            {
                var docNumberKey = await numberSequenceService.GetKeyByTransactionCode(TransactionCode.StorageTransaction);
                var code = await numberSequenceService.GetDocNumber((Guid) docNumberKey);
                var lossTx = new StorageTransaction
                {
                    DocNumberKey = docNumberKey,
                    BranchCode = address.BranchCode,
                    Code = code,
                    StorageAddressCode = address.Code,
                    TransactionDate = processingDate.Date,
                    TransactionTime = DateTime.Now.TimeOfDay.ToString(@"hh\:mm\:ss"),
                    TransactionType = StorageTransactionType.TechnicalLoss,
                    TransactionStatus = StorageTransactionsStatus.Confirmed,
                    CardCode = address.CardCode,
                    CardName = address.CardName,
                    ItemCode = address.ItemCode,
                    ItemName = address.ItemName,
                    UnitOfMeasureCode = address.UoM,
                    GrossWeight = quantityLoss,
                    NetWeight = quantityLoss,
                    WarehouseCode = address.WarehouseCode,
                    WarehouseName = address.WarehouseName,
                    ProcessingCostCode = address.ProcessingCostCode,
                    Comments = $"QUEBRA_TECNICA_{periodStart:yyyyMMdd}_{periodEnd:yyyyMMdd}"
                };

                db.Context.StorageTransactions.Add(lossTx);
            }
        }

        await db.Context.SaveChangesAsync(ct);
    }
}