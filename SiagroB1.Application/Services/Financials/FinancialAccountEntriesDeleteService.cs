using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Exclui lançamento manual. Conciliado fica bloqueado: primeiro se remove a conciliação (reabrir a reconciliação e
/// desmarcar a linha).
/// </summary>
public class FinancialAccountEntriesDeleteService(IUnitOfWork db)
{
    public async Task ExecuteAsync(Guid key)
    {
        var entry = await db.Context.FinancialAccountEntries.FirstOrDefaultAsync(x => x.Key == key)
                    ?? throw new KeyNotFoundException("Lançamento não encontrado.");

        if (entry.TransferKey is not null)
            throw new ApplicationException("Lançamento de transferência: exclua pela transferência.");

        if (entry.ReconciliationKey is not null)
            throw new ApplicationException("Lançamento conciliado não pode ser excluído. Remova a conciliação primeiro.");

        db.Context.FinancialAccountEntries.Remove(entry);
        await db.SaveChangesAsync();
    }
}
