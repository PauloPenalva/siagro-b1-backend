using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.ShipmentReleases;

public class ShipmentReleasesApprovationService(AppDbContext context, ILogger<ShipmentReleasesApprovationService> logger)
{
    public async Task ExecuteAsync(Guid key)
    {
        var sr = await context.ShipmentReleases
            .FindAsync(key) ??
                 throw new NotFoundException($"Shipment Release not found key {key}");
        
        if (sr.Status != ReleaseStatus.Pending)
        {
            throw new ArgumentException("Shipment Release not pending.");
        }
        
        var pc = await context.PurchaseContracts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == sr.PurchaseContractKey) ?? 
                 throw new NotFoundException($"Purchase Contract not found key {sr.PurchaseContractKey}");

        if (pc.Status != ContractStatus.Running)
        {
            throw new ArgumentException("Purchase Contract not running.");
        }
        var totalReleases = GetTotalReleasesByContract(sr.PurchaseContractKey, key);
        
        var avaiable = pc.TotalVolume - totalReleases;

        if (sr.ReleasedQuantity > avaiable)
        {
            throw new ArgumentException("Shipment Release not available");
        }
        
        sr.Status = ReleaseStatus.Approved;
        sr.ExpectedDeliveryDate = pc.DeliveryEndDate;
        sr.AvailableQuantity = sr.ReleasedQuantity;
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