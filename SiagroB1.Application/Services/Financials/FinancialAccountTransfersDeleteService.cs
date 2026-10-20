using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

/// <summary>Exclui a transferência e as duas linhas juntas. Se qualquer ponta estiver conciliada, bloqueia.</summary>
public class FinancialAccountTransfersDeleteService(IUnitOfWork db)
{
    public async Task ExecuteAsync(Guid key)
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
            db.Context.FinancialAccountEntries.RemoveRange(entries);
            db.Context.FinancialAccountTransfers.Remove(transfer);
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
