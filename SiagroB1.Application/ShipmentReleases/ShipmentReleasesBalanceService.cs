using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.ShipmentReleases;

public class ShipmentReleasesBalanceService(IUnitOfWork db, ILogger<ShipmentReleasesBalanceService> logger)
{

    public async Task<ICollection<ShipmentRelesesBalanceResponseDto>> ExecuteAsync(string itemCode)
    {
        var list = await db.Context.ShipmentReleases
            .Include(x => x.PurchaseContract)
            .Include(x => x.Transactions)
            .Where(sr => sr.PurchaseContract.ItemCode == itemCode && sr.Status == ReleaseStatus.Actived)
            .GroupBy(sr => new
            {
                sr.DeliveryLocationCode,
                sr.DeliveryLocationName,
                sr.PurchaseContract.ItemCode,
                sr.PurchaseContract.ItemName,
                sr.PurchaseContract.UnitOfMeasureCode
            })
            .Select(g => new ShipmentRelesesBalanceResponseDto
            {
                DeliveryLocationCode = g.Key.DeliveryLocationCode,
                DeliveryLocationName = g.Key.DeliveryLocationName,
                ItemCode = g.Key.ItemCode,
                ItemName = g.Key.ItemName,
                UnitOfMeasureCode = g.Key.UnitOfMeasureCode,
                ReleasedQuantity = g.Sum(sr => sr.ReleasedQuantity),
                AvailableQuantity = g.Sum(sr =>
                    sr.ReleasedQuantity - sr.Transactions
                        .Where(t =>
                            t.TransactionStatus != StorageTransactionsStatus.Cancelled &&
                            t.TransactionType == StorageTransactionType.SalesShipment ||
                            t.TransactionType == StorageTransactionType.SalesShipmentReturn
                        )
                        .Sum(x => x.NetWeight)
                ),
            })
            .OrderBy(sr => sr.DeliveryLocationName)
            .ToListAsync();
        
        return list;
    }
}