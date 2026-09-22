using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.StorageAddresses;

public class StorageAddressesListOpenedByItemService(IUnitOfWork db)
{
    public async Task<IReadOnlyCollection<StorageAddressBalanceDto>> ExecuteAsync(string itemCode)
    {
        return await db.Context.StorageAddresses
            .AsNoTracking()
            .Include(a => a.Transactions)
            .Where(a =>
                a.ItemCode == itemCode &&
                a.Status == StorageAddressStatus.Open &&
                // Lote de transbordo guarda mercadoria em trânsito, não disponível para
                // expedir por fora: se aparecesse aqui, alguém embarcaria o grão por fora e a
                // carga do transbordo ficaria esperando uma saída que já aconteceu (GAC-1181
                // fase 2).
                a.Nature == StorageAddressNature.Regular)
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
                // 12 com lote = estorno de troca de liberação (GAC-1177); nenhum 12 anterior tem lote.
                Balance = a.Transactions
                    .Where(t =>
                        t.TransactionStatus == StorageTransactionsStatus.Confirmed ||
                        t.TransactionStatus == StorageTransactionsStatus.Invoiced)
                    .Sum(t =>
                        t.TransactionType == StorageTransactionType.Receipt ||
                        t.TransactionType == StorageTransactionType.ShipmentReleased ||
                        t.TransactionType == StorageTransactionType.SalesShipmentReturn
                            ? t.NetWeight
                            : t.TransactionType == StorageTransactionType.Shipment ||
                              t.TransactionType == StorageTransactionType.SalesShipment ||
                              t.TransactionType == StorageTransactionType.TechnicalLoss
                                ? -t.NetWeight
                                : 0)
            })
            .OrderBy(x => x.Code)
            .ToListAsync();
    }
}


