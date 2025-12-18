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
            .Include(x => x.DeliveryLocation)
            .Include(x => x.Transactions)
            .Where(sr => sr.PurchaseContract.ItemCode == itemCode)
            .GroupBy(sr => new
            {
                sr.DeliveryLocationCode,
                sr.DeliveryLocation.Name,
                sr.PurchaseContract.ItemCode,
                sr.PurchaseContract.ItemName,
                sr.PurchaseContract.UnitOfMeasureCode
            })
            .Select(g => new ShipmentRelesesBalanceResponseDto
            {
                DeliveryLocationCode = g.Key.DeliveryLocationCode,
                DeliveryLocationName = g.Key.Name,
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

        // list.Sort((x, z) => 
        //     string.Compare(x.DeliveryLocationName, z.DeliveryLocationName, StringComparison.Ordinal));
        
        return list;
    }
}