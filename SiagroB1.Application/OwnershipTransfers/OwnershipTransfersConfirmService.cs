using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.StorageAddresses;
using SiagroB1.Application.StorageTransactions;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.OwnershipTransfers;

public class OwnershipTransfersConfirmService(
    IUnitOfWork db,
    StorageTransactionsCreateService storageTransactionsCreateService,
    StorageAddressesGetBalanceService balanceService,
    IStringLocalizer<Resource> resource,
    ILogger<OwnershipTransfersConfirmService> logger)
{
    public async Task<OwnershipTransfer?> ExecuteAsync(Guid key, string userName)
    {
        var ownershipTransfer = await db.Context.OwnershipTransfers
                                    .Include(x => x.StorageAddressOrigin)
                                    .Include(x => x.StorageAddressDestination)
                                    .FirstOrDefaultAsync(x => x.Key == key) ??
                             throw new NotFoundException(resource["OWNERSHIP_TRANSFER_NOT_FOUND"]);
        try
        {
            await db.BeginTransactionAsync();

            ValidateOriginBalance(ownershipTransfer);
                
            ownershipTransfer.ApprovedAt = DateTime.Now;
            ownershipTransfer.ApprovedBy = userName;
            ownershipTransfer.TransferStatus = OwnershipTransferStatus.Closed;
            
            await CreateOriginStorageTransaction(ownershipTransfer, userName);
            await CreateDestinationStorageTransaction(ownershipTransfer, userName);
            
            await db.SaveChangesAsync();
            await db.CommitAsync();
        }
        catch (Exception e)
        {
            await db.RollbackAsync();
            
            logger.LogError(e, e.Message);
            throw new DefaultException($"Erro ao atualizar transferencia: {e.Message}");
        }

        return ownershipTransfer;
    }

    private async Task CreateOriginStorageTransaction(OwnershipTransfer ownershipTransfer, string username)
    {
        var storageTransaction = new StorageTransaction
        {
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            NetWeight = ownershipTransfer.Quantity,
            AvaiableVolumeToAllocate = decimal.Zero,
            BranchCode = ownershipTransfer.StorageAddressOrigin.BranchCode,
            StorageAddressCode = ownershipTransfer.StorageAddressOriginCode,
            TransactionType = StorageTransactionType.Shipment,
            CardCode = ownershipTransfer.StorageAddressOrigin?.CardCode,
            CardName = ownershipTransfer.StorageAddressOrigin?.CardName,
            ItemCode = ownershipTransfer.ItemCode,
            ItemName = ownershipTransfer.ItemName,
            UnitOfMeasureCode = ownershipTransfer.UomCode,
            GrossWeight = ownershipTransfer.Quantity,
            WarehouseCode = ownershipTransfer.StorageAddressOrigin?.WarehouseCode,
            WarehouseName = ownershipTransfer.StorageAddressOrigin?.WarehouseName,
            TransactionOrigin = TransactionCode.OwnershipTransfer,
            OwnershipTransferKey = ownershipTransfer.Key,
            Comments = $"Destino da transferencia: Lote {ownershipTransfer.StorageAddressDestinationCode} " +
                       $"de {ownershipTransfer.StorageAddressDestination.CardName}" +
                       $"({ownershipTransfer.StorageAddressDestination.CardCode})",
            IsOwnershipTransfer = "Y",
        };

        await storageTransactionsCreateService.ExecuteAsync(storageTransaction, username, TransactionCode.OwnershipTransfer, CommitMode.Deferred);
    }
    
    private async Task CreateDestinationStorageTransaction(OwnershipTransfer ownershipTransfer, string username)
    {
        var storageTransaction = new StorageTransaction
        {
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            NetWeight = ownershipTransfer.Quantity,
            AvaiableVolumeToAllocate = decimal.Zero,
            BranchCode = ownershipTransfer.StorageAddressDestination.BranchCode,
            StorageAddressCode = ownershipTransfer.StorageAddressDestinationCode,
            TransactionType = StorageTransactionType.Receipt,
            CardCode = ownershipTransfer.StorageAddressDestination?.CardCode,
            CardName = ownershipTransfer.StorageAddressDestination?.CardName,
            ItemCode = ownershipTransfer.ItemCode,
            ItemName = ownershipTransfer.ItemName,
            UnitOfMeasureCode = ownershipTransfer.UomCode,
            GrossWeight = ownershipTransfer.Quantity,
            WarehouseCode = ownershipTransfer.StorageAddressDestination?.WarehouseCode,
            WarehouseName = ownershipTransfer.StorageAddressDestination?.WarehouseName,
            TransactionOrigin = TransactionCode.OwnershipTransfer,
            OwnershipTransferKey = ownershipTransfer.Key,
            Comments = $"Origem da transferencia: Lote {ownershipTransfer.StorageAddressOriginCode} " +
                       $"de {ownershipTransfer.StorageAddressOrigin.CardName}" +
                       $"({ownershipTransfer.StorageAddressOrigin.CardCode})",
            IsOwnershipTransfer = "Y"
        };

        await storageTransactionsCreateService.ExecuteAsync(storageTransaction, username, TransactionCode.OwnershipTransfer, CommitMode.Deferred);
    }
    
    private void ValidateOriginBalance(OwnershipTransfer ownershipTransfer)
    {
        var stCode = ownershipTransfer.StorageAddressOriginCode;
        var balance = balanceService.GetBalance(stCode);

        if (ownershipTransfer.Quantity > balance)
        {
            throw new ApplicationException(resource["OWNERSHIP_TRANSFER_ORIGIN_BALANCE"]);
        }
        
    }
}