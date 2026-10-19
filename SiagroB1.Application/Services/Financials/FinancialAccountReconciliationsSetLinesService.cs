using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

/// <summary>Referência a uma linha de movimento: a fonte diz em que tabela procurar a chave.</summary>
public record MovementRef(FinancialAccountMovementSource Source, Guid Key);

/// <summary>Marca/desmarca linhas (baixas e lançamentos manuais) numa reconciliação ABERTA.</summary>
public class FinancialAccountReconciliationsSetLinesService(IUnitOfWork db)
{
    public async Task ExecuteAsync(Guid reconciliationKey, IReadOnlyList<MovementRef> mark,
        IReadOnlyList<MovementRef> unmark)
    {
        var reconciliation = await FinancialAccountReconciliationsLoader.LoadAsync(db, reconciliationKey);

        if (reconciliation.Status != FinancialAccountReconciliationStatus.Open)
            throw new ApplicationException("A reconciliação já está fechada.");

        foreach (var line in mark)
        {
            var (account, date, current, set) = await ResolveAsync(line);

            if (account != reconciliation.FinancialAccountCode)
                throw new ApplicationException("Lançamento de outra conta.");

            if (date.Date > reconciliation.StatementDate.Date)
                throw new ApplicationException("Lançamento posterior à data do extrato.");

            if (current is not null && current != reconciliation.Key)
                throw new ApplicationException("Lançamento já conciliado em outra reconciliação.");

            set(reconciliation.Key);
        }

        foreach (var line in unmark)
        {
            var (_, _, current, set) = await ResolveAsync(line);
            if (current == reconciliation.Key) set(null);
        }

        await db.SaveChangesAsync();
    }

    private async Task<(string? Account, DateTime Date, Guid? Current, Action<Guid?> Set)> ResolveAsync(MovementRef line)
    {
        if (line.Source == FinancialAccountMovementSource.Settlement)
        {
            FinancialSettlement s = await db.Context.FinancialSettlements.FirstOrDefaultAsync(x => x.Key == line.Key)
                                    ?? throw new KeyNotFoundException("Baixa não encontrada.");
            return (s.FinancialAccountCode, s.SettlementDate, s.ReconciliationKey, k => s.ReconciliationKey = k);
        }

        FinancialAccountEntry e = await db.Context.FinancialAccountEntries.FirstOrDefaultAsync(x => x.Key == line.Key)
                                  ?? throw new KeyNotFoundException("Lançamento não encontrado.");
        return (e.FinancialAccountCode, e.EntryDate, e.ReconciliationKey, k => e.ReconciliationKey = k);
    }
}

internal static class FinancialAccountReconciliationsLoader
{
    public static async Task<FinancialAccountReconciliation> LoadAsync(IUnitOfWork db, Guid key) =>
        await db.Context.FinancialAccountReconciliations.FirstOrDefaultAsync(x => x.Key == key)
        ?? throw new KeyNotFoundException("Reconciliação não encontrada.");
}
