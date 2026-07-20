using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageEntryTransactions.Factories;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Services.StorageTransactions.Factories;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.StorageEntryTransactions;

/// <summary>
/// Entrada de compra em armazenagem própria. Cria o par Purchase/Receipt:
/// o Purchase baixa contrato e liberação (igual ao embarque), o Receipt alimenta
/// o saldo do lote em nome da empresa.
/// </summary>
public class StorageEntryTransactionsCreateService(
    IUnitOfWork unitOfWork,
    StorageTransactionsCreateService storageCreateService,
    StorageTransactionsConfirmedService storageConfirmedService,
    PurchaseContractsAllocationCreateService purchaseAllocationCreateService,
    ShipmentReleasesRecalculateShippedService recalcShipped)
{
    public async Task<StorageEntryTransaction> ExecuteAsync(
        Guid purchaseContractKey,
        string storageAddressCode,
        StorageTransaction purchase,
        string userName)
    {
        var lot = await unitOfWork.Context.StorageAddresses
                      .FirstOrDefaultAsync(x => x.Code == storageAddressCode)
                  ?? throw new NotFoundException($"Lote de armazenagem {storageAddressCode} não encontrado.");

        StorageEntryReceiptFactory.EnsureLotAccepts(lot, purchase);

        try
        {
            await unitOfWork.BeginTransactionAsync();

            // ---- Lado da compra: idêntico ao embarque ----
            await storageCreateService.ExecuteAsync(
                purchase, userName, TransactionCode.StorageTransaction, CommitMode.Deferred);

            await storageConfirmedService.ExecuteAsync(
                purchase, userName, CommitMode.Deferred, isShipmentTransaction: true);

            await purchaseAllocationCreateService.ExecuteAsync(
                purchaseContractKey, purchase, purchase.NetWeight, userName, CommitMode.Deferred);

            // ---- Lado da armazenagem ----
            // Copia e converte ANTES de criar, para que CardName/WarehouseName sejam
            // resolvidos já com os códigos do lote.
            var receipt = StorageTransactionCopyFactory.CreateFrom(purchase, userName);
            StorageEntryReceiptFactory.ApplyLot(receipt, lot);

            await storageCreateService.ExecuteAsync(
                receipt, userName, TransactionCode.StorageTransaction, CommitMode.Deferred);

            await storageConfirmedService.ExecuteAsync(receipt, userName, CommitMode.Deferred);

            var entry = new StorageEntryTransaction
            {
                PurchaseStorageTransaction = purchase,
                ReceiptStorageTransaction = receipt,
                PurchaseContractKey = purchaseContractKey,
                StorageAddressCode = lot.Code,
                Status = StorageEntryTransactionStatus.Confirmed,
                AllocatedVolume = purchase.NetWeight,
                ReceiptNetWeight = receipt.NetWeight,
                CreatedBy = userName,
                CreatedAt = DateTime.Now,
            };

            await unitOfWork.Context.StorageEntryTransactions.AddAsync(entry);

            await unitOfWork.SaveChangesAsync();
            await unitOfWork.CommitAsync();

            // Fora da transação e explícito: os hooks de ShippedQuantity em
            // Create/Confirmed só disparam em CommitMode.Auto, e todo o fluxo acima
            // roda em Deferred — sem esta chamada a liberação não seria atualizada.
            if (purchase.ShipmentReleaseKey.HasValue)
                await recalcShipped.RecalculateAsync(purchase.ShipmentReleaseKey.Value);

            return entry;
        }
        catch (Exception e)
        {
            await unitOfWork.RollbackAsync();
            throw new ApplicationException(e.Message);
        }
    }
}
