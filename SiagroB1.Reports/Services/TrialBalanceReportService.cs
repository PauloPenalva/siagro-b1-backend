using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services.Accounting;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Balancete (CTBR040): saldo anterior, débito, crédito e saldo atual por conta. A conta sintética soma as inferiores
/// pela conta superior. Conta sem saldo e sem movimento fica de fora.
/// </summary>
public class TrialBalanceReportService(IUnitOfWork db, IFastReportService reportService)
{
    private sealed class Totals
    {
        public decimal Opening;
        public decimal Debit;
        public decimal Credit;
    }

    public static string? Validate(TrialBalanceReportRequest request) =>
        request.DateFrom.Date > request.DateTo.Date ? "A data inicial deve ser menor ou igual à final." : null;

    public async Task<List<TrialBalanceReportRowDto>> RowsAsync(TrialBalanceReportRequest request)
    {
        if (Validate(request) is { } error) throw new ApplicationException(error);

        var filter = new AccountPostingFilter(request.BranchCode, request.AccountFrom, request.AccountTo,
            request.CostCenterCode, request.AccountingItemCode);
        var postings = await new AccountPostingReader(db).ReadAsync(null, request.DateTo, filter);
        var accounts = await db.Context.LedgerAccounts.AsNoTracking()
            .Select(a => new { a.Code, a.Name, a.ParentCode, a.AllowsPosting })
            .ToDictionaryAsync(a => a.Code);
        var from = request.DateFrom.Date;

        var totals = new Dictionary<string, Totals>();
        foreach (var p in postings)
        {
            // Sobe a cadeia de superiores somando em cada nível (com trava contra ciclo legado).
            var code = p.AccountCode;
            var seen = new HashSet<string>();
            while (code is not null && seen.Add(code))
            {
                if (!totals.TryGetValue(code, out var t)) totals[code] = t = new Totals();
                if (p.Date < from) t.Opening += p.Debit - p.Credit;
                else
                {
                    t.Debit += p.Debit;
                    t.Credit += p.Credit;
                }

                code = accounts.TryGetValue(code, out var a) ? a.ParentCode : null;
            }
        }

        int Depth(string code)
        {
            var depth = 1;
            var seen = new HashSet<string> { code };
            while (accounts.TryGetValue(code, out var a) && a.ParentCode is not null && seen.Add(a.ParentCode))
            {
                depth++;
                code = a.ParentCode;
            }

            return depth;
        }

        var rows = new List<TrialBalanceReportRowDto>();
        foreach (var (code, t) in totals.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var moved = t.Debit != 0 || t.Credit != 0;
            if (!moved && (t.Opening == 0 || request.OnlyWithMovement)) continue;

            var depth = Depth(code);
            if (request.Level is { } max && depth > max) continue;

            var (opening, openingSide) = AccountPostingReader.Display(t.Opening);
            var (closing, closingSide) = AccountPostingReader.Display(t.Opening + t.Debit - t.Credit);
            rows.Add(new TrialBalanceReportRowDto
            {
                AccountCode = code,
                AccountName = accounts.TryGetValue(code, out var a) ? a.Name : string.Empty,
                Level = depth,
                Analytic = a?.AllowsPosting ?? true,
                Opening = opening,
                OpeningSide = openingSide,
                Debit = t.Debit,
                Credit = t.Credit,
                Closing = closing,
                ClosingSide = closingSide,
            });
        }

        return rows;
    }

    public async Task<byte[]> ExecuteAsync(TrialBalanceReportRequest request)
    {
        var rows = await RowsAsync(request);
        var analytic = rows.Where(r => r.Analytic).ToList();
        var parameters = new Dictionary<string, object>
        {
            ["pPeriod"] = $"Período: {request.DateFrom:dd/MM/yyyy} a {request.DateTo:dd/MM/yyyy}",
            ["pFilters"] = AccountingReportFilters.Describe(request.BranchCode, request.AccountFrom, request.AccountTo,
                request.CostCenterCode, request.AccountingItemCode),
            ["pTotalDebit"] = analytic.Sum(r => r.Debit),
            ["pTotalCredit"] = analytic.Sum(r => r.Credit),
        };
        return await reportService.GeneratePdfAsync("AccountingTrialBalance.frx", rows, "TrialRows", "TrialRows", parameters);
    }
}
