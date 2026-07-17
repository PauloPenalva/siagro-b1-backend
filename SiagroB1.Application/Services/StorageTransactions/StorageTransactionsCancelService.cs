using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.StorageTransactions;

public class StorageTransactionsCancelService(
    IUnitOfWork db,
    ShipmentReleasesRecalculateShippedService recalcShipped)
{
    public async Task ExecuteAsync(Guid key, string username, TransactionCode transactionCode = TransactionCode.StorageTransaction)
    {
        var doc = await db.Context.StorageTransactions
            .FirstOrDefaultAsync(x => x.Key == key) ??
                  throw new NotFoundException("Storage transaction not found.");

        if (doc.TransactionOrigin != transactionCode)
        {
            var msg =
                "This storage transaction is created by another transaction. It cannot be canceled using this method.\n" +
                "Transaction Origin: " + doc.TransactionOrigin;

            throw new ApplicationException(msg);
        }

        if (doc.TransactionStatus == StorageTransactionsStatus.Invoiced)
        {
            throw new ApplicationException("Storage transaction is invoiced. Please, cancel assinged invoice first.");
        }

        var hasAllocations = await db.Context.PurchaseContractsAllocations
            .AnyAsync(x => x.StorageTransactionKey == key);

        if (hasAllocations)
        {
            throw new ApplicationException(
                "This storage transaction has purchase contract allocations. Please remove them before canceling.");
        }

        try
        {
            doc.TransactionStatus = StorageTransactionsStatus.Cancelled;
            await db.SaveChangesAsync();

            if (doc.ShipmentReleaseKey.HasValue &&
                doc.TransactionType is StorageTransactionType.SalesShipment or StorageTransactionType.SalesShipmentReturn)
            {
                await recalcShipped.RecalculateAsync(doc.ShipmentReleaseKey.Value);
            }
        }
        catch (Exception e)
        {
            throw new ApplicationException(e.Message);
        }
    }
}