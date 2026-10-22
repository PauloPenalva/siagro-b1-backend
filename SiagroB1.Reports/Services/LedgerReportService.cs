using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services.Accounting;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Razão contábil (CTBR400): por conta, saldo anterior e partidas do período com saldo corrente e indicador D/C.
/// Conta sem saldo anterior e sem movimento fica de fora.
/// </summary>
public class LedgerReportService(IUnitOfWork db, IFastReportService reportService)
{
    public static string? Validate(LedgerReportRequest request) =>
        request.DateFrom.Date > request.DateTo.Date ? "A data inicial deve ser menor ou igual à final." : null;

    public async Task<List<LedgerReportRowDto>> RowsAsync(LedgerReportRequest request)
    {
        if (Validate(request) is { } error) throw new ApplicationException(error);

        var filter = new AccountPostingFilter(request.BranchCode, request.AccountFrom, request.AccountTo,
            request.CostCenterCode, request.AccountingItemCode);
        var postings = await new AccountPostingReader(db).ReadAsync(null, request.DateTo, filter);
        var names = await db.Context.LedgerAccounts.AsNoTracking().ToDictionaryAsync(a => a.Code, a => a.Name);
        var from = request.DateFrom.Date;

        var rows = new List<LedgerReportRowDto>();
        foreach (var account in postings.GroupBy(p => p.AccountCode).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var balance = account.Where(p => p.Date < from).Sum(p => p.Debit - p.Credit);
            var period = account.Where(p => p.Date >= from)
                .OrderBy(p => p.Date).ThenBy(p => p.DocEntry).ThenBy(p => p.LineNumber).ToList();
            if (balance == 0 && period.Count == 0) continue;

            var name = names.GetValueOrDefault(account.Key, string.Empty);
            var (openValue, openSide) = AccountPostingReader.Display(balance);
            rows.Add(new LedgerReportRowDto
            {
                AccountCode = account.Key, AccountName = name, History = "Saldo anterior", Balance = openValue, BalanceSide = openSide,
            });

            foreach (var p in period)
            {
                balance += p.Debit - p.Credit;
                var (value, side) = AccountPostingReader.Display(balance);
                rows.Add(new LedgerReportRowDto
                {
                    AccountCode = account.Key,
                    AccountName = name,
                    Date = p.Date.ToString("dd/MM/yyyy"),
                    Document = $"{p.Batch}/{p.DocumentNumber}",
                    BranchCode = p.BranchCode,
                    History = p.History,
                    Counterpart = p.Counterpart ?? string.Empty,
                    CostCenterCode = p.CostCenterCode ?? string.Empty,
                    Debit = p.Debit,
                    Credit = p.Credit,
                    Balance = value,
                    BalanceSide = side,
                });
            }
        }

        return rows;
    }

    public async Task<byte[]> ExecuteAsync(LedgerReportRequest request)
    {
        var rows = await RowsAsync(request);
        var parameters = new Dictionary<string, object>
        {
            ["pPeriod"] = $"Período: {request.DateFrom:dd/MM/yyyy} a {request.DateTo:dd/MM/yyyy}",
            ["pFilters"] = AccountingReportFilters.Describe(request.BranchCode, request.AccountFrom, request.AccountTo,
                request.CostCenterCode, request.AccountingItemCode),
            ["pTotalDebit"] = rows.Sum(r => r.Debit),
            ["pTotalCredit"] = rows.Sum(r => r.Credit),
        };
        return await reportService.GeneratePdfAsync("AccountingLedger.frx", rows, "LedgerRows", "LedgerRows", parameters);
    }
}

internal static class AccountingReportFilters
{
    public static string Describe(string? branch, string? from, string? to, string? costCenter, string? item)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(branch)) parts.Add($"Filial {branch}");
        if (!string.IsNullOrWhiteSpace(from) || !string.IsNullOrWhiteSpace(to)) parts.Add($"Contas {from} a {to}".Trim());
        if (!string.IsNullOrWhiteSpace(costCenter)) parts.Add($"Centro de custo {costCenter}");
        if (!string.IsNullOrWhiteSpace(item)) parts.Add($"Item contábil {item}");
        return parts.Count == 0 ? "Todas as filiais e contas" : string.Join(" · ", parts);
    }
}
