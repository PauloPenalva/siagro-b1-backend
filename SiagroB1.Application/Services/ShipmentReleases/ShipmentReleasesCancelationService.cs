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
                     .Include(x => x.Transactions)
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
        
        if (sr.HasStorageTransactions)
        {
            var msg = "Shipment Release has storage transaction(s) confirmed.\n";
            msg += "Please, cancel storage transaction(s) code(s):\n";
            msg = sr.Transactions.Aggregate(msg, (current, storageTransaction) => current + $"- {storageTransaction.Code}\n");
        
            throw new ApplicationException(msg);
        }
        
        sr.Status = ReleaseStatus.Cancelled;
        sr.UpdatedBy = string.Empty;
        sr.UpdatedAt = DateTime.Now;
        
        await context.SaveChangesAsync();
    }
}