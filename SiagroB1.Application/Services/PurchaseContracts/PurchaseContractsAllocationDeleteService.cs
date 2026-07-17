using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsAllocationDeleteService(
    IUnitOfWork db,
    ILogger<PurchaseContractsAllocationDeleteService> logger)
{
    public async Task ExecuteWithTransactionAsync(Guid key, string userName)
    {
        // Estorno manual (tela): contrato encerrado não aceita movimentação.
        // O cascade interno (ExecuteAsync, ex.: ShipmentBillingDelete) não passa por aqui.
        var alloc = await db.Context.PurchaseContractsAllocations
                        .FirstOrDefaultAsync(x => x.Key == key)
            ?? throw new NotFoundException("Purchase contract allocation not found.");

        var contract = await db.Context.PurchaseContracts
            .FirstOrDefaultAsync(x => x.Key == alloc.PurchaseContractKey);

        if (contract?.Status == ContractStatus.Finished)
            throw new ApplicationException("Contrato encerrado: não é possível estornar a alocação.");

        try
        {
            await db.BeginTransactionAsync();
            await ExecuteAsync(key, userName);
            await db.CommitAsync();
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
    }

    public async Task ExecuteAsync(Guid key, string userName, CommitMode commitMode = CommitMode.Auto)
    {
        var alloc = await db.Context.PurchaseContractsAllocations
                        .FirstOrDefaultAsync(x => x.Key == key)
            ?? throw new NotFoundException("Purchase contract allocation not found.");

        var storageTransaction = await db.Context.StorageTransactions
                                     .FirstOrDefaultAsync(x => x.Key == alloc.StorageTransactionKey)
            ?? throw new NotFoundException("Storage transaction not found.");

        if (storageTransaction.TransactionStatus == StorageTransactionsStatus.Invoiced)
            throw new ApplicationException("Cannot delete an allocation of an invoiced storage transaction.");

        db.Context.PurchaseContractsAllocations.Remove(alloc);

        // Deriva o saldo do total que RESTA alocado (exclui a alocação removida),
        // em vez de incrementar — fonte única de verdade, sem drift.
        var remainingAllocated = await db.Context.PurchaseContractsAllocations
            .Where(x => x.StorageTransactionKey == storageTransaction.Key && x.Key != alloc.Key)
            .SumAsync(x => decimal.Abs(x.Volume));

        storageTransaction.RecalculateAvailableVolume(remainingAllocated);

        // Recalcula o saldo alocado do CONTRATO (Volume com sinal, exclui a removida).
        var contract = await db.Context.PurchaseContracts
            .FirstOrDefaultAsync(x => x.Key == alloc.PurchaseContractKey);

        if (contract != null)
        {
            var contractRemaining = await db.Context.PurchaseContractsAllocations
                .Where(x => x.PurchaseContractKey == alloc.PurchaseContractKey && x.Key != alloc.Key)
                .SumAsync(x => x.Volume);

            contract.AllocatedVolume = contractRemaining;
        }

        if (commitMode == CommitMode.Auto)
            await db.SaveChangesAsync();

        logger.LogInformation(
            "Purchase contract allocation {AllocationKey} (contract {PurchaseContractKey}, volume {Volume}) deleted by {UserName}",
            alloc.Key, alloc.PurchaseContractKey, alloc.Volume, userName);
    }
}
