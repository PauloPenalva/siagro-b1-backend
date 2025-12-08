using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.StorageAddresses;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.ShippingOrders;

public class ShippingOrdersShippedService(
    AppDbContext db,
    StorageAddressesTotalsService storageAddressesTotalsService
    )
{
    public async Task ExecuteAsync(Guid key)
    {
        var order = await db.ShippingOrders
                        .FirstOrDefaultAsync(x => x.Key == key) ??
                    throw new NotFoundException("Shipping order not found.");

        if (order.Status != ShippingOrderStatus.Planned)
        {
            throw new ApplicationException("Shipping order not in planned status.");
        }

        await ValidateStorageAddressBalance(order);

        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            await CreateStorageTransaction(order);
            
            order.Status = ShippingOrderStatus.Shipped;
            
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception e)
        {
            throw new ApplicationException(e.Message);
        }
    }

    private async Task ValidateStorageAddressBalance(ShippingOrder order)
    {
        var storageAddressTotals = await storageAddressesTotalsService.ExecuteAsync(order.StorageAddressKey);

        if (order.Volume > storageAddressTotals.Balance)
        {
            var msg = $"Shipping order volume exceeds balance of storage address.\n";
            msg += $"Storage address volume avaiable is {order.StorageAddress?.Balance}.";
            
            throw new ApplicationException(msg);
        }
    }

    private async Task CreateStorageTransaction(ShippingOrder order)
    {
        var existingEntiy = await db.StorageTransactions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ShippingOrderKey == order.Key);

        if (existingEntiy != null)
        {
            throw new ApplicationException("Storage transaction linked to the shipping order already exists.");
        }
        
        var st = new StorageTransaction
        {
            StorageAddressKey = order.StorageAddressKey,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            GrossWeight = order.Volume,
            TransactionOrigin = TransactionCode.ShippingOrder,
            ShippingOrderKey = order.Key,
            TruckCode = order.TruckCode,
            TruckDriverCode = order.TruckDriverCode,
            CardCode = "",
            ItemCode = "",
            UnitOfMeasureCode = "",
            NetWeight = order.Volume,
            WarehouseCode = "",
        };

        db.StorageTransactions.Add(st);
        await db.SaveChangesAsync();
    }
}