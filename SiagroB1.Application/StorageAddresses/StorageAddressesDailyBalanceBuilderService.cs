using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.StorageAddresses;

public class StorageAddressesDailyBalanceBuilderService(IUnitOfWork db)
{
     public async Task BuildAsync(DateTime date, CancellationToken ct = default)
    {
        var addresses = await db.Context.StorageAddresses
            .Where(x => x.Status == StorageAddressStatus.Open)
            .ToListAsync(ct);

        foreach (var address in addresses)
        {
            var previous = await db.Context.StorageDailyBalances
                .Where(x => x.StorageAddressCode == address.Code && x.BalanceDate < date.Date)
                .OrderByDescending(x => x.BalanceDate)
                .FirstOrDefaultAsync(ct);

            var opening = previous?.ClosingBalance ?? 0m;

            var movements = await db.Context.StorageTransactions
                .Where(x =>
                    x.StorageAddressCode == address.Code &&
                    x.TransactionDate == date.Date &&
                    (x.TransactionStatus == StorageTransactionsStatus.Confirmed ||
                     x.TransactionStatus == StorageTransactionsStatus.Invoiced))
                .ToListAsync(ct);

            var receiptQty = movements
                .Where(x => x.TransactionType == StorageTransactionType.Receipt)
                .Sum(x => x.NetWeight);

            var shipmentQty = movements
                .Where(x => x.TransactionType == StorageTransactionType.Shipment)
                .Sum(x => x.NetWeight);
            
            var technicalLossQty = movements
                .Where(x => x.TransactionType == StorageTransactionType.TechnicalLoss)
                .Sum(x => x.NetWeight);

            var closing = opening + receiptQty - shipmentQty - technicalLossQty;
            
            var existing = await db.Context.StorageDailyBalances
                .FirstOrDefaultAsync(x => x.StorageAddressCode == address.Code && x.BalanceDate == date.Date, ct);

            if (existing == null)
            {
                existing = new StorageDailyBalance
                {
                    StorageAddressCode = address.Code,
                    BalanceDate = date.Date
                };

                db.Context.StorageDailyBalances.Add(existing);
            }
            
            existing.OpeningBalance = opening;
            existing.ReceiptQty = receiptQty;
            existing.ShipmentQty = shipmentQty;
            existing.TechnicalLossQty = technicalLossQty;
            existing.ClosingBalance = closing;
            existing.BillableBalance = closing < 0 ? 0 : closing;

            //db.Context.StorageDailyBalances.Add(daily);
        }

        await db.Context.SaveChangesAsync(ct);
    }
}