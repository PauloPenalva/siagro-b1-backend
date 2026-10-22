using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

/// <summary>Exclui (CANCELA, sem apagar) a transferência e as duas linhas juntas. Se qualquer ponta estiver conciliada, bloqueia.</summary>
public class FinancialAccountTransfersDeleteService(IUnitOfWork db, SiagroB1.Application.Services.Accounting.Postings.IAccountingPostingService? accounting = null)
{
    public async Task ExecuteAsync(Guid key, string? userName = null)
    {
        var transfer = await db.Context.FinancialAccountTransfers.FirstOrDefaultAsync(x => x.Key == key)
                       ?? throw new KeyNotFoundException("Transferência não encontrada.");

        var entries = await db.Context.FinancialAccountEntries.Where(x => x.TransferKey == key).ToListAsync();

        if (entries.Any(x => x.ReconciliationKey is not null))
            throw new ApplicationException(
                "Transferência com lançamento conciliado não pode ser excluída. Remova a conciliação primeiro.");

        try
        {
            await db.BeginTransactionAsync();
            var now = DateTime.Now;
            var user = userName ?? "Unknown";
            foreach (var entry in entries)
            {
                entry.CanceledAt = now;
                entry.CanceledBy = user;
            }
            transfer.CanceledAt = now;
            transfer.CanceledBy = user;
            await SiagroB1.Application.Services.Accounting.Postings.AccountEntryPosting.CancelTransferAsync(accounting, db.Context, transfer, user);
            await db.Context.SaveChangesAsync();
            await db.CommitAsync();
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
    }
}
