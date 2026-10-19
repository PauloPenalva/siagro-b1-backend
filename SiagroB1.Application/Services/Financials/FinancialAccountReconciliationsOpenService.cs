using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Abre a reconciliação da conta numa data de extrato. O saldo de abertura é o conciliado até aqui: soma das linhas
/// marcadas em reconciliações FECHADAS da conta.
/// </summary>
public class FinancialAccountReconciliationsOpenService(IUnitOfWork db)
{
    public async Task<FinancialAccountReconciliation> ExecuteAsync(string accountCode, DateTime statementDate,
        decimal statementBalance, string? notes, string userName)
    {
        var code = accountCode?.Trim().ToUpperInvariant() ?? string.Empty;

        if (!await db.Context.FinancialAccounts.AnyAsync(x => x.Code == code))
            throw new ApplicationException($"Conta financeira {code} não encontrada.");

        var reconciliations = db.Context.FinancialAccountReconciliations.Where(x => x.FinancialAccountCode == code);

        if (await reconciliations.AnyAsync(x => x.Status == FinancialAccountReconciliationStatus.Open))
            throw new ApplicationException("Já existe uma reconciliação aberta para esta conta.");

        var lastClosed = await reconciliations
            .Where(x => x.Status == FinancialAccountReconciliationStatus.Closed)
            .OrderByDescending(x => x.StatementDate)
            .Select(x => (DateTime?)x.StatementDate)
            .FirstOrDefaultAsync();

        var date = statementDate.Date;
        if (lastClosed is not null && date <= lastClosed.Value.Date)
            throw new ApplicationException(
                $"A data do extrato deve ser posterior à da última reconciliação fechada ({lastClosed.Value:dd/MM/yyyy}).");

        var reconciliation = new FinancialAccountReconciliation
        {
            FinancialAccountCode = code,
            StatementDate = date,
            StatementBalance = decimal.Round(statementBalance, 2, MidpointRounding.ToEven),
            OpeningBalance = await ReconciledBalanceAsync(db, code),
            Status = FinancialAccountReconciliationStatus.Open,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            CreatedBy = userName,
            UpdatedBy = userName
        };

        db.Context.FinancialAccountReconciliations.Add(reconciliation);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Duplo clique: os dois passaram pelo AnyAsync e o índice único filtrado barrou o segundo.
            db.Context.Entry(reconciliation).State = EntityState.Detached;
            throw new ApplicationException("Já existe uma reconciliação aberta para esta conta.");
        }
        return reconciliation;
    }

    /// <summary>Soma das linhas conciliadas em reconciliações fechadas da conta.</summary>
    internal static async Task<decimal> ReconciledBalanceAsync(IUnitOfWork db, string accountCode)
    {
        var closedKeys = db.Context.FinancialAccountReconciliations
            .Where(x => x.FinancialAccountCode == accountCode && x.Status == FinancialAccountReconciliationStatus.Closed)
            .Select(x => (Guid?)x.Key);

        return await FinancialAccountMovementsQuery.Query(db.Context, accountCode)
            .Where(r => r.ReconciliationKey != null && closedKeys.Contains(r.ReconciliationKey))
            .SumAsync(r => r.Amount);
    }
}
