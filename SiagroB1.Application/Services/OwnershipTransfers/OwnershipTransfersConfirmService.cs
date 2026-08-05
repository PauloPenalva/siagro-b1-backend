using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.OwnershipTransfers.Factories;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.OwnershipTransfers;

public class OwnershipTransfersConfirmService(
    IUnitOfWork db,
    StorageTransactionsCreateService storageTransactionsCreateService,
    OwnershipTransfersValidateContractService validateContractService,
    PurchaseContractsAllocationCreateService allocationCreateService,
    IStorageAddressBalanceReader balanceReader,
    IStringLocalizer<Resource> resource,
    ILogger<OwnershipTransfersConfirmService> logger)
{
    public async Task<OwnershipTransfer?> ExecuteAsync(Guid key, string userName)
    {
        var ownershipTransfer = await db.Context.OwnershipTransfers
                                    .Include(x => x.StorageAddressOrigin)
                                    .Include(x => x.StorageAddressDestination)
                                    .FirstOrDefaultAsync(x => x.Key == key) ??
                             throw new NotFoundException(resource["OWNERSHIP_TRANSFER_NOT_FOUND"].Value);

        // Fora do try: o catch abaixo embrulha tudo em DefaultException, o que
        // esconderia a mensagem de negócio. E antes de BeginTransaction, para que
        // uma rejeição não abra transação nenhuma.
        Validate(ownershipTransfer);

        var contract = await validateContractService.ExecuteAsync(
            ownershipTransfer,
            ownershipTransfer.StorageAddressOrigin!,
            ownershipTransfer.StorageAddressDestination!);

        try
        {
            await db.BeginTransactionAsync();

            ownershipTransfer.ApprovedAt = DateTime.Now;
            ownershipTransfer.ApprovedBy = userName;
            ownershipTransfer.TransferStatus = OwnershipTransferStatus.Closed;

            await CreateOriginStorageTransaction(ownershipTransfer, userName);
            await CreateDestinationStorageTransaction(ownershipTransfer, userName);

            if (contract != null)
            {
                CreateShipmentRelease(ownershipTransfer, contract, userName);
                await CreatePurchaseAllocationAsync(ownershipTransfer, contract, userName);
            }

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
    /// Emite a liberação de embarque do contrato. É o lado COMERCIAL da transferência:
    /// o par Shipment/Receipt acima é apenas custódia.
    /// </summary>
    /// <remarks>
    /// A liberação nasce COM saldo: o físico já está em nosso poder, mas a mercadoria
    /// ainda precisa ser embarcada para faturamento.
    ///
    /// Não chama <c>ShipmentReleasesRecalculateShippedService</c>: o romaneio de compra
    /// criado em <see cref="CreatePurchaseAllocationAsync"/> não aponta para a liberação,
    /// justamente para que <c>ShippedQuantity</c> continue zerado.
    /// </remarks>
    private void CreateShipmentRelease(
        OwnershipTransfer ownershipTransfer, PurchaseContract contract, string userName)
    {
        var release = OwnershipTransferShipmentReleaseFactory.CreateFrom(
            ownershipTransfer, ownershipTransfer.StorageAddressDestination!, contract, userName);

        db.Context.ShipmentReleases.Add(release);

        // Também na coleção em memória: TotalAvailableToRelease deriva dela, e sem isso
        // o contrato rastreado nesta mesma unidade de trabalho ainda enxergaria o saldo
        // antigo.
        contract.ShipmentReleases.Add(release);
    }

    /// <summary>
    /// Debita o saldo físico do contrato: a mercadoria FOI entregue, ela só não veio
    /// de caminhão. Cria o romaneio de compra e o aloca ao contrato.
    /// </summary>
    /// <remarks>
    /// Dois pontos que parecem detalhe e são a regra:
    ///
    /// 1. O romaneio nasce SEM <c>ShipmentReleaseKey</c>. <c>CalculateShippedAsync</c>
    ///    só soma romaneios ligados à liberação, então o <c>ShippedQuantity</c> dela
    ///    continua zerado e ela mantém o saldo a carregar — que é o ponto: o grão foi
    ///    entregue, mas ainda precisa ser embarcado para faturamento.
    ///
    /// 2. Como o contrato já é debitado aqui, a Expedição de Grãos NÃO pode alocar de
    ///    novo ao embarcar esta liberação (ver <c>ShippingTransactionsCreateService</c>).
    ///    Sem esse par de regras, 50 mil kg de grão debitariam 100 mil do contrato.
    /// </remarks>
    private async Task CreatePurchaseAllocationAsync(
        OwnershipTransfer ownershipTransfer, PurchaseContract contract, string userName)
    {
        var destination = ownershipTransfer.StorageAddressDestination!;

        var purchase = new StorageTransaction
        {
            TransactionDate = ownershipTransfer.Date,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            TransactionType = StorageTransactionType.Purchase,
            NetWeight = ownershipTransfer.Quantity,
            GrossWeight = ownershipTransfer.Quantity,
            BranchCode = destination.BranchCode,

            // Fornecedor do CONTRATO, não o dono do lote: é dele que estamos comprando.
            CardCode = contract.CardCode,
            CardName = contract.CardName,

            ItemCode = ownershipTransfer.ItemCode,
            ItemName = ownershipTransfer.ItemName,
            UnitOfMeasureCode = ownershipTransfer.UomCode,
            WarehouseCode = destination.WarehouseCode,
            WarehouseName = destination.WarehouseName,

            // Perna comercial: sem lote, igual ao Purchase(8) da Expedição de Grãos.
            // O lote já foi movimentado pelo par Shipment/Receipt de custódia.
            StorageAddressCode = null,

            TransactionOrigin = TransactionCode.OwnershipTransfer,
            OwnershipTransferKey = ownershipTransfer.Key,
            IsOwnershipTransfer = "Y",
            Comments = $"Compra por transferencia de propriedade {ownershipTransfer.TransferCode}",
        };

        await storageTransactionsCreateService.ExecuteAsync(
            purchase, userName, TransactionCode.OwnershipTransfer, CommitMode.Deferred);

        // Semeia o saldo alocável como o StorageTransactionsConfirmedService faria —
        // aqui o romaneio já nasce Confirmed e não passa por aquele serviço.
        purchase.AvaiableVolumeToAllocate = purchase.NetWeight;

        await allocationCreateService.ExecuteAsync(
            contract.Key, purchase, purchase.NetWeight, userName, CommitMode.Deferred);
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
            TransactionDate = ownershipTransfer.Date,
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
    
    /// <summary>
    /// Guardas de confirmação. A de status é a mais importante: sem ela uma
    /// transferência já <c>Closed</c> podia ser confirmada de novo, duplicando a
    /// movimentação de lote — e, com contrato vinculado, duplicando também a
    /// liberação de embarque e o consumo do contrato.
    /// </summary>
    private void Validate(OwnershipTransfer ownershipTransfer)
    {
        if (ownershipTransfer.TransferStatus != OwnershipTransferStatus.Open)
            throw new ApplicationException(resource["OWNERSHIP_TRANSFER_NOT_OPEN"].Value);

        var origin = ownershipTransfer.StorageAddressOrigin
                     ?? throw new NotFoundException(resource["OWNERSHIP_TRANSFER_NOT_FOUND"].Value);
        var destination = ownershipTransfer.StorageAddressDestination
                          ?? throw new NotFoundException(resource["OWNERSHIP_TRANSFER_NOT_FOUND"].Value);

        if (string.Equals(ownershipTransfer.StorageAddressOriginCode,
                ownershipTransfer.StorageAddressDestinationCode, StringComparison.OrdinalIgnoreCase))
            throw new ApplicationException(resource["OWNERSHIP_TRANSFER_SAME_ADDRESS"].Value);

        if (ownershipTransfer.Quantity <= decimal.Zero)
            throw new ApplicationException(resource["OWNERSHIP_TRANSFER_INVALID_QUANTITY"].Value);

        EnsureLotAccepts(origin, ownershipTransfer);
        EnsureLotAccepts(destination, ownershipTransfer);

        ValidateOriginBalance(ownershipTransfer);
    }

    private void EnsureLotAccepts(StorageAddress lot, OwnershipTransfer ownershipTransfer)
    {
        if (lot.Status == StorageAddressStatus.Closed)
            throw new ApplicationException(resource["OWNERSHIP_TRANSFER_LOT_CLOSED"].Value);

        if (!string.Equals(lot.ItemCode, ownershipTransfer.ItemCode, StringComparison.OrdinalIgnoreCase))
            throw new ApplicationException(resource["OWNERSHIP_TRANSFER_ITEM_MISMATCH"].Value);

        if (!string.Equals(lot.UoM, ownershipTransfer.UomCode, StringComparison.OrdinalIgnoreCase))
            throw new ApplicationException(resource["OWNERSHIP_TRANSFER_UOM_MISMATCH"].Value);
    }

    private void ValidateOriginBalance(OwnershipTransfer ownershipTransfer)
    {
        var stCode = ownershipTransfer.StorageAddressOriginCode;
        var balance = balanceReader.GetBalance(stCode);

        if (ownershipTransfer.Quantity > balance)
        {
            throw new ApplicationException(resource["OWNERSHIP_TRANSFER_ORIGIN_BALANCE"].Value);
        }

    }
}