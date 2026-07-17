using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentReleases;

public class ShipmentReleasesRecalculateShippedService(AppDbContext context)
{
    public async Task RecalculateAsync(Guid shipmentReleaseKey)
    {
        var release = await context.ShipmentReleases
            .FirstOrDefaultAsync(x => x.Key == shipmentReleaseKey);

        if (release is null)
            return;

        // usado = Σ(SalesShipment.Net) − Σ(SalesShipmentReturn.Net); Pending conta, Cancelled não.
        var shipped = await context.StorageTransactions
            .Where(t => t.ShipmentReleaseKey == shipmentReleaseKey
                        && t.TransactionStatus != StorageTransactionsStatus.Cancelled
                        && (t.TransactionType == StorageTransactionType.SalesShipment
                            || t.TransactionType == StorageTransactionType.SalesShipmentReturn))
            .SumAsync(t => t.TransactionType == StorageTransactionType.SalesShipment
                ? t.NetWeight
                : -t.NetWeight);

        release.ShippedQuantity = shipped;

        await context.SaveChangesAsync();
    }
}
