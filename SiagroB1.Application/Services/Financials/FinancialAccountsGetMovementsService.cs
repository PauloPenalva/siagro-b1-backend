using SiagroB1.Domain.Dtos;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Application.Services.Financials;

/// <summary>Lançamentos da conta no período (baixas + manuais) com saldo anterior e acumulado — a tela de lançamentos.</summary>
public class FinancialAccountsGetMovementsService(IUnitOfWork db)
{
    public async Task<FinancialAccountMovementsDto> ExecuteAsync(string accountCode, DateTime dateFrom, DateTime dateTo,
        bool onlyReconciled = false, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accountCode))
            throw new ApplicationException("Informe a conta financeira.");

        if (dateFrom.Date > dateTo.Date)
            throw new ApplicationException("A data inicial deve ser menor ou igual à final.");

        return await FinancialAccountMovementsQuery.StatementAsync(
            db.Context, accountCode.Trim().ToUpperInvariant(), dateFrom, dateTo, onlyReconciled, ct);
    }
}
