using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Application.Services.StorageTransactions.Factories;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.StorageTransactions;

public class StorageTransactionsCopyService(
    IUnitOfWork unitOfWork,
    DocNumberSequenceService numberSequenceService,
    StorageTransactionsCreateService createService,
    IStringLocalizer<Resource> resource)
{
    public async Task<StorageTransaction> ExecuteAsync(Guid key, string userName, CommitMode commitMode = CommitMode.Auto)
    {
        var original = await unitOfWork.Context.StorageTransactions
            .AsNoTracking()
            .Include(x => x.QualityInspections)
            .FirstOrDefaultAsync(x => x.Key == key) ??
                               throw new NotFoundException("Storage transaction not found.");

        EnsureCopyable(original);

        var clone = StorageTransactionCopyFactory.CreateFrom(original, userName);

        await createService.ExecuteAsync(clone, userName, TransactionCode.StorageTransaction, commitMode);

        return clone;
    }

    public async Task<StorageTransaction> ExecuteAsync(StorageTransaction original, string userName, CommitMode commitMode = CommitMode.Auto)
    {
        EnsureCopyable(original);

        var clone = StorageTransactionCopyFactory.CreateFrom(original, userName);

        await createService.ExecuteAsync(clone, userName, TransactionCode.StorageTransaction, commitMode);

        return clone;
    }

    // Romaneio de Perda/Sobra de armazém só nasce pela aprovação da Conferência de Saldo
    // (StorageTransactionsWarehouseBalanceService, GAC-1164). Copiá-lo criaria um Pendente
    // solto, sem conferência dona, que ninguém consegue confirmar (StorageTransactionsConfirmedService
    // recusa os tipos 13/14). O guard roda antes de qualquer transação/try.
    private void EnsureCopyable(StorageTransaction original)
    {
        if (original.TransactionType is StorageTransactionType.WarehouseLoss or StorageTransactionType.WarehouseGain)
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_TRANSACTION_NOT_COPYABLE"].Value);
    }
}