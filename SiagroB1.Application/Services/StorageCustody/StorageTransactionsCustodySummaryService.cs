using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Application.Services.StorageCustody;

/// <summary>Seção "Notas fiscais de custódia" do romaneio (spec 2026-10-12 §5): mesmas regras do relatório.</summary>
public class StorageTransactionsCustodySummaryService(IUnitOfWork db)
{
    public async Task<StorageCustodySummaryDto> ExecuteAsync(Guid key)
    {
        if (!await db.Context.StorageTransactions.AsNoTracking().AnyAsync(t => t.Key == key))
            throw new NotFoundException("Romaneio não encontrado.");

        var transaction = await StorageCustodyQuery.Transactions(db.Context).FirstOrDefaultAsync(t => t.Key == key);
        if (transaction is null)
            return new StorageCustodySummaryDto { TransactionKey = key };

        var steps = StorageCustodyRules.BuildSteps(transaction, await StorageCustodyQuery.ActiveDocumentsAsync(db.Context, [key]));

        return new StorageCustodySummaryDto
        {
            TransactionKey = key,
            Applies = steps.Count > 0,
            Steps = steps,
            NextRole = StorageCustodyRules.NextRole(steps),
        };
    }
}
