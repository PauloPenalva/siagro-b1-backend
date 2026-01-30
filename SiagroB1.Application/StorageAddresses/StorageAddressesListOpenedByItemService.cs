using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.StorageAddresses;

public class StorageAddressesListOpenedByItemService(IUnitOfWork db)
{
    public async Task<IReadOnlyCollection<StorageAddressBalanceDto>> ExecuteAsync(string itemCode)
    {
        return await db.Context.StorageAddresses
            .AsNoTracking()
            .Include(a => a.Transactions)
            .Where(a =>
                a.ItemCode == itemCode &&
                a.Status == StorageAddressStatus.Open)
            .Select(a => new StorageAddressBalanceDto
            { 
                Code = a.Code,
                CreationDate = a.CreationDate,
                Description = a.Description,
                CardCode = a.CardCode,
                CardName = a.CardName,
                ItemCode = a.ItemCode,
                ItemName = a.ItemName,
                WarehouseCode = a.WarehouseCode,
                WarehouseName = a.WarehouseName,
                Balance = a.Transactions
                    .Where(t =>
                        t.TransactionStatus == StorageTransactionsStatus.Confirmed ||
                        t.TransactionStatus == StorageTransactionsStatus.Invoiced)
                    .Sum(t =>
                        t.TransactionType == StorageTransactionType.Receipt ||
                        t.TransactionType == StorageTransactionType.ShipmentReleased
                            ? t.NetWeight
                            : t.TransactionType == StorageTransactionType.Shipment ||
                              t.TransactionType == StorageTransactionType.SalesShipment ||
                              t.TransactionType == StorageTransactionType.QualityLoss
                                ? -t.NetWeight
                                : 0)
            })
            .OrderBy(x => x.Code)
            .ToListAsync();
    }
}


