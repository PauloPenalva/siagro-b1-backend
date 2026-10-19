using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

/// <summary>Exclui reconciliação ABERTA, liberando as linhas marcadas nela.</summary>
public class FinancialAccountReconciliationsDeleteService(IUnitOfWork db)
{
    public async Task ExecuteAsync(Guid key)
    {
        var r = await FinancialAccountReconciliationsLoader.LoadAsync(db, key);

        if (r.Status != FinancialAccountReconciliationStatus.Open)
            throw new ApplicationException("Reconciliação fechada não pode ser excluída. Reabra-a primeiro.");

        foreach (var s in await db.Context.FinancialSettlements.Where(x => x.ReconciliationKey == key).ToListAsync())
            s.ReconciliationKey = null;

        foreach (var e in await db.Context.FinancialAccountEntries.Where(x => x.ReconciliationKey == key).ToListAsync())
            e.ReconciliationKey = null;

        db.Context.FinancialAccountReconciliations.Remove(r);
        await db.SaveChangesAsync();
    }
}
