using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.ShipmentReleases;

public class ShipmentReleasesCancelationService(AppDbContext context, ILogger<ShipmentReleasesCancelationService> logger)
{
    public async Task ExecuteAsync(Guid key)
    {
        var sr = await context.ShipmentReleases
            .FindAsync(key) ??
                 throw new NotFoundException($"Shipment Release not found key {key}");
        
        if (sr.Status is ReleaseStatus.Cancelled or ReleaseStatus.Completed )
        {
            throw new ArgumentException("Shipment Release is cancelled or completed.");
        }
        
        sr.Status = ReleaseStatus.Cancelled;
        sr.ApprovedBy = string.Empty;
        sr.ApprovedAt = DateTime.Now;

        await context.SaveChangesAsync();
    }

    private decimal GetTotalReleasesByContract(Guid contractKey, Guid releaseKey)
    {
        return context.ShipmentReleases
            .Where(x => x.PurchaseContractKey == contractKey &&
                        x.Status != ReleaseStatus.Cancelled &&
                        x.Key != releaseKey)
            .Sum(x => x.ReleasedQuantity);
    }
}