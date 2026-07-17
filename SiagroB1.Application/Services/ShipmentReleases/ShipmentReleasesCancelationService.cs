using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentReleases;

public class ShipmentReleasesCancelationService(AppDbContext context, ILogger<ShipmentReleasesCancelationService> logger)
{
    public async Task ExecuteAsync(Guid key)
    {
        var sr = await context.ShipmentReleases
                     .FirstOrDefaultAsync(x => x.Key == key) ??
                 throw new NotFoundException($"Shipment Release not found key {key}");

        var contract = await context.PurchaseContracts
            .FirstOrDefaultAsync(x => x.Key == sr.PurchaseContractKey);

        if (contract?.Status == ContractStatus.Finished)
            throw new ApplicationException("Contrato encerrado: não é possível cancelar a liberação de embarque.");

        if (sr.Status is ReleaseStatus.Cancelled or ReleaseStatus.Completed or ReleaseStatus.Paused)
        {
            throw new ApplicationException("Shipment Release is not in Activated state.");
        }
        
        var blockingCodes = await context.StorageTransactions
            .Where(t => t.ShipmentReleaseKey == sr.Key &&
                        t.TransactionStatus != StorageTransactionsStatus.Cancelled &&
                        (t.TransactionType == StorageTransactionType.SalesShipment ||
                         t.TransactionType == StorageTransactionType.SalesShipmentReturn ||
                         t.TransactionType == StorageTransactionType.Purchase ||
                         t.TransactionType == StorageTransactionType.PurchaseReturn))
            .Select(t => t.Code)
            .ToListAsync();

        if (blockingCodes.Count > 0)
        {
            var msg = "Shipment Release has storage transaction(s) confirmed.\n"
                      + "Please, cancel storage transaction(s) code(s):\n"
                      + string.Join("\n", blockingCodes.Select(c => $"- {c}"));

            throw new ApplicationException(msg);
        }
        
        sr.Status = ReleaseStatus.Cancelled;
        sr.UpdatedBy = string.Empty;
        sr.UpdatedAt = DateTime.Now;
        
        await context.SaveChangesAsync();
    }
}