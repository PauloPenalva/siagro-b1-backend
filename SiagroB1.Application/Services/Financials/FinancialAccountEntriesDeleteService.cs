using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Exclui lançamento manual: CANCELA (CanceledAt), não apaga — o rastro e o estorno contábil dependem da linha. Conciliado fica bloqueado: primeiro se remove a conciliação (reabrir a reconciliação e
/// desmarcar a linha).
/// </summary>
public class FinancialAccountEntriesDeleteService(IUnitOfWork db, SiagroB1.Application.Services.Accounting.Postings.IAccountingPostingService? accounting = null)
{
    public async Task ExecuteAsync(Guid key, string? userName = null)
    {
        var entry = await db.Context.FinancialAccountEntries.FirstOrDefaultAsync(x => x.Key == key)
                    ?? throw new KeyNotFoundException("Lançamento não encontrado.");

        if (entry.TransferKey is not null)
            throw new ApplicationException("Lançamento de transferência: exclua pela transferência.");

        if (entry.ReconciliationKey is not null)
            throw new ApplicationException("Lançamento conciliado não pode ser excluído. Remova a conciliação primeiro.");

        entry.CanceledAt = DateTime.Now;
        entry.CanceledBy = userName ?? "Unknown";
        await SiagroB1.Application.Services.Accounting.Postings.AccountEntryPosting.CancelEntryAsync(accounting, db.Context, entry, entry.CanceledBy);
        await db.SaveChangesAsync();
    }
}
