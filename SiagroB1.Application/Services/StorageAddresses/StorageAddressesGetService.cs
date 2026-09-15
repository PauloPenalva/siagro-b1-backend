using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.StorageAddresses;

public class StorageAddressesGetService(IUnitOfWork db, ILogger<StorageAddressesGetService> logger)
{
    public async Task<StorageAddress?> GetByIdAsync(string code)
    {
        try
        {
            return await db.Context.StorageAddresses
                .Include(x => x.Transactions)
                .Include(x => x.ProcessingCost)
                .FirstOrDefaultAsync(x => x.Code == code);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,"Error fetching entity with ID {Id}", code);
            throw new DefaultException("Error fetching entity");
        }
    }

    /// <summary>
    /// Base da lista de lotes e do value help de lote. O saldo sai somado do banco: carregar os
    /// romaneios de cada lote fazia o plano varrer STORAGE_TRANSACTIONS inteira, e sem
    /// READ_COMMITTED_SNAPSHOT qualquer romaneio travado, até de outro lote, segurava a tela até o
    /// timeout. A regra da soma é a mesma de <see cref="StorageAddress.Balance"/>.
    /// </summary>
    public IQueryable<StorageAddress> QueryAll()
    {
        return db.Context.StorageAddresses
            .AsNoTracking()
            .Select(a => new StorageAddress
            {
                Code = a.Code,
                RowId = a.RowId,
                BranchCode = a.BranchCode,
                Branch = a.Branch,
                DocNumberKey = a.DocNumberKey,
                DocNumber = a.DocNumber,
                CreatedAt = a.CreatedAt,
                CreatedBy = a.CreatedBy,
                UpdatedAt = a.UpdatedAt,
                UpdatedBy = a.UpdatedBy,
                ApprovedAt = a.ApprovedAt,
                ApprovedBy = a.ApprovedBy,
                CanceledAt = a.CanceledAt,
                CanceledBy = a.CanceledBy,
                CreationDate = a.CreationDate,
                OwnershipType = a.OwnershipType,
                Description = a.Description,
                CardCode = a.CardCode,
                CardName = a.CardName,
                ItemCode = a.ItemCode,
                ItemName = a.ItemName,
                WarehouseCode = a.WarehouseCode,
                WarehouseName = a.WarehouseName,
                PurchaseContractKey = a.PurchaseContractKey,
                TransactionOrigin = a.TransactionOrigin,
                Status = a.Status,
                UoM = a.UoM,
                ProcessingCostCode = a.ProcessingCostCode,
                ProcessingCost = a.ProcessingCost,
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
                              t.TransactionType == StorageTransactionType.TechnicalLoss
                                ? -t.NetWeight
                                : 0)
            });
    }
}