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
/// <para>
/// <b>GAC-1214 — liberação cujo físico JÁ está em nosso poder</b>
/// (<see cref="ReleaseOriginRules.ShipsWithoutPurchaseLeg"/>: devolução ao armazém, transbordo,
/// sobra). O contrato já foi debitado e o armazém já foi creditado antes; aqui o par vira
/// SalesShipment/Receipt, sem alocação — igual à Expedição de Grãos. O Purchase(8) baixaria o
/// contrato de novo, creditaria o armazém de novo e não consumiria a liberação.
/// </para>
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

        var release = purchase.ShipmentReleaseKey.HasValue
            ? await unitOfWork.Context.ShipmentReleases.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Key == purchase.ShipmentReleaseKey.Value)
            : null;
        var semPernaDeCompra = release != null && ReleaseOriginRules.ShipsWithoutPurchaseLeg(release.Origin);

        try
        {
            await unitOfWork.BeginTransactionAsync();

            if (semPernaDeCompra)
            {
                // ---- Saída do armazém onde o grão já está (ver <summary>) ----
                purchase.TransactionType = StorageTransactionType.SalesShipment;
                purchase.TransactionStatus = StorageTransactionsStatus.Pending;
                purchase.ShipmentLoadKey = null;
                purchase.SalesInvoiceKey = null;

                await storageCreateService.ExecuteAsync(
                    purchase, userName, TransactionCode.StorageTransaction, CommitMode.Deferred);

                await storageConfirmedService.ExecuteAsync(
                    purchase, userName, CommitMode.Deferred, isShipmentTransaction: true);
            }
            else
            {
                // ---- Lado da compra: idêntico ao embarque ----
                await storageCreateService.ExecuteAsync(
                    purchase, userName, TransactionCode.StorageTransaction, CommitMode.Deferred);

                await storageConfirmedService.ExecuteAsync(
                    purchase, userName, CommitMode.Deferred, isShipmentTransaction: true);

                await purchaseAllocationCreateService.ExecuteAsync(
                    purchaseContractKey, purchase, purchase.NetWeight, userName, CommitMode.Deferred);
            }

            // ---- Lado da armazenagem ----
            // Copia e converte ANTES de criar, para que CardName/WarehouseName sejam
            // resolvidos já com os códigos do lote.
            var receipt = StorageTransactionCopyFactory.CreateFrom(purchase, userName);
            StorageEntryReceiptFactory.ApplyLot(receipt, lot);
            StorageEntryReceiptFactory.MoveFreightToReceipt(purchase, receipt);

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
                AllocatedVolume = semPernaDeCompra ? decimal.Zero : purchase.NetWeight,
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
