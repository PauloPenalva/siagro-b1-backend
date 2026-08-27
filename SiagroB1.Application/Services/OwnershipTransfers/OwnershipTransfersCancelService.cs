using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.OwnershipTransfers;

public class OwnershipTransfersCancelService(
    IUnitOfWork db,
    StorageTransactionsCreateService storageTransactionsCreateService,
    ShipmentReleasesCancelationService releasesCancelationService,
    ShipmentReleasesRecalculateShippedService recalcShipped,
    PurchaseContractsAllocationDeleteService allocationDeleteService,
    StorageTransactionsCancelService storageCancelService,
    IStorageAddressBalanceReader balanceReader,
    IStringLocalizer<Resource> resource,
    ILogger<OwnershipTransfersCancelService> logger)
{
    public async Task<OwnershipTransfer?> ExecuteAsync(Guid key, string userName)
    {
        var ownershipTransfer = await db.Context.OwnershipTransfers
                                    .Include(x => x.StorageAddressOrigin)
                                    .Include(x => x.StorageAddressDestination)
                                    .FirstOrDefaultAsync(x => x.Key == key) ??
                             throw new NotFoundException(resource["OWNERSHIP_TRANSFER_NOT_FOUND"].Value);

        // Fora do try: o catch abaixo embrulha tudo em DefaultException e esconderia
        // a mensagem de negócio.
        if (ownershipTransfer.TransferStatus == OwnershipTransferStatus.Cancelled)
            throw new ApplicationException(resource["OWNERSHIP_TRANSFER_ALREADY_CANCELLED"].Value);

        // Também fora do try, e antes do saldo do lote: com embarque parcial o lote
        // fica curto e ValidateDestinationBalance falharia com uma mensagem enganosa
        // sobre saldo, em vez de apontar o embarque que é a causa real.
        var release = await FindCancellableReleaseAsync(ownershipTransfer);

        try
        {
            await db.BeginTransactionAsync();

            if (ownershipTransfer.TransferStatus == OwnershipTransferStatus.Closed)
            {
                if (release != null)
                    await CancelShipmentReleaseAsync(release, ownershipTransfer, userName);

                // Devolve o saldo físico ao contrato antes de mexer no lote: o
                // cancelamento do romaneio de compra é recusado enquanto existir
                // alocação apontando para ele.
                await ReversePurchaseAllocationAsync(ownershipTransfer, userName);

                ValidateDestinationBalance(ownershipTransfer);

                await CreateDestinationStorageTransaction(ownershipTransfer, userName);
                await CreateOriginStorageTransaction(ownershipTransfer, userName);
            }
            
            ownershipTransfer.CanceledAt = DateTime.Now;
            ownershipTransfer.CanceledBy = userName;
            ownershipTransfer.TransferStatus = OwnershipTransferStatus.Cancelled;
            
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

    /// <summary>
    /// Localiza a liberação emitida pela confirmação e recusa o cancelamento se ela já
    /// tiver embarque.
    /// </summary>
    /// <remarks>
    /// O volume vem de <c>CalculateShippedAsync</c>, e não da coluna persistida:
    /// <c>ShippedQuantity</c> é derivada e pode estar defasada — é justamente por isso
    /// que o serviço de recálculo existe.
    /// </remarks>
    private async Task<ShipmentRelease?> FindCancellableReleaseAsync(OwnershipTransfer ownershipTransfer)
    {
        if (ownershipTransfer.TransferStatus != OwnershipTransferStatus.Closed)
            return null;

        var release = await db.Context.ShipmentReleases
            .FirstOrDefaultAsync(x => x.OwnershipTransferKey == ownershipTransfer.Key &&
                                      x.Status != ReleaseStatus.Cancelled);

        if (release == null)
            return null;

        if (await recalcShipped.CalculateShippedAsync(release.Key, release.Origin) > decimal.Zero)
            throw new ApplicationException(
                resource["OWNERSHIP_TRANSFER_RELEASE_ALREADY_SHIPPED"].Value);

        return release;
    }

    /// <summary>
    /// Desfaz o lado COMERCIAL: cancela a liberação de embarque, devolvendo o saldo ao
    /// contrato. Delega para não duplicar as regras de restituição — o serviço de
    /// cancelamento participa da transação ambiente, já que o UnitOfWork compartilha o
    /// mesmo AppDbContext.
    /// </summary>
    private async Task CancelShipmentReleaseAsync(
        ShipmentRelease release, OwnershipTransfer ownershipTransfer, string userName) =>
        await releasesCancelationService.ExecuteAsync(
            release.Key,
            userName,
            $"Cancelamento da transferência de propriedade {ownershipTransfer.TransferCode}",
            allowOwnershipTransferOrigin: true);

    /// <summary>
    /// Desfaz a baixa do saldo físico: remove a alocação e cancela o romaneio de compra
    /// gerado pela confirmação. A ordem importa — <c>StorageTransactionsCancelService</c>
    /// recusa cancelar romaneio que ainda tenha alocação de contrato apontando para ele.
    /// </summary>
    private async Task ReversePurchaseAllocationAsync(
        OwnershipTransfer ownershipTransfer, string userName)
    {
        var purchase = await db.Context.StorageTransactions
            .FirstOrDefaultAsync(x => x.OwnershipTransferKey == ownershipTransfer.Key &&
                                      x.TransactionType == StorageTransactionType.Purchase &&
                                      x.TransactionStatus != StorageTransactionsStatus.Cancelled);

        if (purchase == null)
            return;

        var allocations = await db.Context.PurchaseContractsAllocations
            .Where(x => x.StorageTransactionKey == purchase.Key)
            .Select(x => x.Key)
            .ToListAsync();

        // CommitMode.Auto de propósito: o guard de StorageTransactionsCancelService
        // consulta as alocações no banco, e um Remove ainda não gravado continuaria
        // visível para ele. Estamos dentro da transação do cancelamento, então o
        // SaveChanges aqui não escapa do rollback.
        foreach (var allocationKey in allocations)
            await allocationDeleteService.ExecuteAsync(allocationKey, userName);

        // A origem precisa bater: o serviço recusa cancelar romaneio criado por outro
        // fluxo, e este nasceu com TransactionOrigin = OwnershipTransfer.
        await storageCancelService.ExecuteAsync(
            purchase.Key, userName, TransactionCode.OwnershipTransfer);
    }

    private async Task CreateOriginStorageTransaction(OwnershipTransfer ownershipTransfer, string username)
    {
        var storageTransaction = new StorageTransaction
        {
            TransactionDate = ownershipTransfer.Date,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            NetWeight = ownershipTransfer.Quantity,
            AvaiableVolumeToAllocate = decimal.Zero,
            BranchCode = ownershipTransfer.StorageAddressOrigin.BranchCode,
            StorageAddressCode = ownershipTransfer.StorageAddressOriginCode,
            TransactionType = StorageTransactionType.Receipt,
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
            TransactionDate = ownershipTransfer.Date,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            NetWeight = ownershipTransfer.Quantity,
            AvaiableVolumeToAllocate = decimal.Zero,
            BranchCode = ownershipTransfer.StorageAddressDestination.BranchCode,
            StorageAddressCode = ownershipTransfer.StorageAddressDestinationCode,
            TransactionType = StorageTransactionType.Shipment,
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
            IsOwnershipTransfer = "Y",
        };

        await storageTransactionsCreateService.ExecuteAsync(storageTransaction, username, TransactionCode.OwnershipTransfer, CommitMode.Deferred);
    }

    private void ValidateDestinationBalance(OwnershipTransfer ownershipTransfer)
    {
        var stCode = ownershipTransfer.StorageAddressDestinationCode;
        var balance = balanceReader.GetBalance(stCode);

        if (ownershipTransfer.Quantity > balance)
        {
            throw new ApplicationException(resource["OWNERSHIP_TRANSFER_DESTINATION_BALANCE"].Value);
        }
        
    }
}