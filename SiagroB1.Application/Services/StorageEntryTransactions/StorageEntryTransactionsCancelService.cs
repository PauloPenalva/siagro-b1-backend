using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.StorageEntryTransactions;

/// <summary>
/// Estorna a entrada de compra em armazenagem própria: devolve o volume ao contrato
/// e tira o produto do lote, cancelando os dois romaneios do par.
/// </summary>
public class StorageEntryTransactionsCancelService(
    IUnitOfWork unitOfWork,
    PurchaseContractsAllocationDeleteService allocationDeleteService,
    StorageTransactionsCancelService storageCancelService,
    IStorageAddressBalanceReader balanceReader)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var entry = await unitOfWork.Context.StorageEntryTransactions
                        .FirstOrDefaultAsync(x => x.Key == key)
                    ?? throw new NotFoundException("Entrada em armazenagem não encontrada.");

        if (entry.Status == StorageEntryTransactionStatus.Cancelled)
            throw new ApplicationException("Esta entrada em armazenagem já está cancelada.");

        var purchase = await unitOfWork.Context.StorageTransactions
                           .FirstOrDefaultAsync(x => x.Key == entry.PurchaseStorageTransactionKey)
                       ?? throw new NotFoundException("Romaneio de compra não encontrado.");

        var receipt = await unitOfWork.Context.StorageTransactions
                          .FirstOrDefaultAsync(x => x.Key == entry.ReceiptStorageTransactionKey)
                      ?? throw new NotFoundException("Romaneio de recebimento não encontrado.");

        if (purchase.TransactionStatus == StorageTransactionsStatus.Invoiced ||
            receipt.TransactionStatus == StorageTransactionsStatus.Invoiced)
            throw new ApplicationException(
                "Romaneio faturado: cancele a nota fiscal antes de estornar a entrada.");

        var contract = await unitOfWork.Context.PurchaseContracts
            .FirstOrDefaultAsync(x => x.Key == entry.PurchaseContractKey);

        if (contract?.Status == ContractStatus.Finished)
            throw new ApplicationException(
                "Contrato encerrado: não é possível estornar a entrada em armazenagem.");

        // O produto precisa continuar no lote para que a entrada possa ser desfeita.
        if (!string.IsNullOrEmpty(entry.StorageAddressCode) &&
            balanceReader.GetBalance(entry.StorageAddressCode) < entry.ReceiptNetWeight)
            throw new ApplicationException(
                $"Saldo insuficiente no lote {entry.StorageAddressCode}: o produto desta entrada já foi movimentado.");

        try
        {
            await unitOfWork.BeginTransactionAsync();

            // A alocação sai primeiro — o cancelamento do romaneio recusa enquanto
            // existir alocação de contrato apontando para ele.
            var allocations = await unitOfWork.Context.PurchaseContractsAllocations
                .Where(x => x.StorageTransactionKey == purchase.Key)
                .Select(x => x.Key)
                .ToListAsync();

            foreach (var allocationKey in allocations)
                await allocationDeleteService.ExecuteAsync(allocationKey, userName);

            await storageCancelService.ExecuteAsync(receipt.Key, userName);
            await storageCancelService.ExecuteAsync(purchase.Key, userName);

            entry.Status = StorageEntryTransactionStatus.Cancelled;
            entry.CanceledBy = userName;
            entry.CanceledAt = DateTime.Now;

            await unitOfWork.SaveChangesAsync();
            await unitOfWork.CommitAsync();
        }
        catch (Exception e)
        {
            await unitOfWork.RollbackAsync();
            throw new ApplicationException(e.Message);
        }
    }
}
