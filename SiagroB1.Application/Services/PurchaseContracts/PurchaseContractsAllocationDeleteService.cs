using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsAllocationDeleteService(
    IUnitOfWork db,
    ILogger<PurchaseContractsAllocationDeleteService> logger)
{
    public async Task ExecuteWithTransactionAsync(Guid key, string userName)
    {
        // Estorno manual (tela): contrato encerrado não aceita movimentação.
        // O cascade interno (ExecuteAsync, ex.: ShippingTransactionsReverse) não passa por aqui.
        var alloc = await db.Context.PurchaseContractsAllocations
                        .FirstOrDefaultAsync(x => x.Key == key)
            ?? throw new NotFoundException("Purchase contract allocation not found.");

        var contract = await db.Context.PurchaseContracts
            .FirstOrDefaultAsync(x => x.Key == alloc.PurchaseContractKey);

        if (contract?.Status == ContractStatus.Finished)
            throw new ApplicationException("Contrato encerrado: não é possível estornar a alocação.");

        // Troca nota: a alocação de um romaneio de quebra/sobra de descarga (16/17) só pode ser
        // desfeita reabrindo a Conferência de Entregas — soltá-la por aqui deixaria o romaneio
        // ativo, sem alocação. O cascade interno da sync (ExecuteAsync) não passa por este guard.
        var dischargeType = await db.Context.StorageTransactions
            .Where(x => x.Key == alloc.StorageTransactionKey)
            .Select(x => (StorageTransactionType?)x.TransactionType)
            .FirstOrDefaultAsync();

        if (dischargeType is StorageTransactionType.PurchaseDischargeLoss or StorageTransactionType.PurchaseDischargeGain)
            throw new ApplicationException(
                "Esta alocação é de quebra/sobra de descarga da troca nota. Reabra a Conferência de Entregas para desfazê-la.");

        // A alocação de uma entrada em armazenagem faz parte de um agregado: soltá-la
        // aqui devolveria o volume ao contrato mas deixaria o produto no lote e a
        // liberação consumida. O estorno da entrada é o único caminho consistente
        // (e ele passa por ExecuteAsync, sem tocar neste guard).
        var storageEntry = await db.Context.StorageEntryTransactions
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.PurchaseStorageTransactionKey == alloc.StorageTransactionKey &&
                x.Status == StorageEntryTransactionStatus.Confirmed);

        if (storageEntry != null)
            throw new ApplicationException(
                "Esta alocação pertence a uma entrada em armazenagem própria. " +
                "Estorne a entrada correspondente em vez de excluir a alocação.");

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

        // Spec 2026-10-13 (títulos firmes, regras 23–24): o estorno do romaneio desfaz a composição dos firmes dele,
        // remove os vínculos fiscais com documentos deste contrato e cancela os firmes — ou recusa, se o provisório já
        // tem baixa. ANTES de qualquer outra mudança; tudo enfileirado no mesmo SaveChanges do estorno.
        await FinancialDocumentsFirmUnwind.EnqueueForAllocationAsync(
            db.Context, storageTransaction, alloc.PurchaseContractKey, userName);

        db.Context.PurchaseContractsAllocations.Remove(alloc);

        // Deriva o saldo do total que RESTA alocado (exclui a alocação removida),
        // em vez de incrementar — fonte única de verdade, sem drift.
        var remainingAllocated = await db.Context.PurchaseContractsAllocations
            .Where(x => x.StorageTransactionKey == storageTransaction.Key && x.Key != alloc.Key)
            .SumAsync(x => decimal.Abs(x.Volume));

        var changedQuantity = await PurchaseTransactionChangeQuery.ChangedQuantityAsync(
            db.Context, storageTransaction.Key);

        storageTransaction.RecalculateAvailableVolume(remainingAllocated, changedQuantity);

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
