using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Linhas candidatas da reconciliação: da conta, com data até a do extrato, pendentes ou marcadas NESTA reconciliação.
/// Traz também o quadro de saldos (abertura, marcado, diferença).
/// </summary>
public class FinancialAccountReconciliationsGetLinesService(IUnitOfWork db)
{
    public async Task<FinancialAccountReconciliationLinesDto> ExecuteAsync(Guid key)
    {
        var r = await FinancialAccountReconciliationsLoader.LoadAsync(db, key);
        var limit = r.StatementDate.Date.AddDays(1);

        var rows = await FinancialAccountMovementsQuery.Query(db.Context, r.FinancialAccountCode)
            .Where(x => x.Date < limit && (x.ReconciliationKey == null || x.ReconciliationKey == key))
            .OrderBy(x => x.Date).ThenBy(x => x.CreatedAt)
            .ToListAsync();

        var dtos = rows.Select(x => FinancialAccountMovementsQuery.ToDto(x, marked: x.ReconciliationKey == key)).ToList();
        var reconciled = dtos.Where(x => x.Marked).Sum(x => x.Amount);

        return new FinancialAccountReconciliationLinesDto
        {
            OpeningBalance = r.OpeningBalance,
            StatementBalance = r.StatementBalance,
            ReconciledAmount = reconciled,
            Difference = r.OpeningBalance + reconciled - r.StatementBalance,
            Rows = dtos
        };
    }
}
