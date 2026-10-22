using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services.Accounting;

namespace SiagroB1.Reports.Services;

/// <summary>
/// DRE (CTBR510) pela visão gerencial: período, acumulado no ano e % sobre a receita; opcionalmente por centro de
/// custo. Ignora a apuração de resultado e os estornos dela, senão o mês apurado sairia zerado.
/// </summary>
public class IncomeStatementReportService(IUnitOfWork db, IFastReportService reportService)
{
    public const string NoCenter = "Sem centro";

    public static string? Validate(IncomeStatementReportRequest request) =>
        request.DateFrom.Date > request.DateTo.Date ? "A data inicial deve ser menor ou igual à final."
        : string.IsNullOrWhiteSpace(request.ViewCode) ? "Escolha a visão gerencial."
        : null;

    public async Task<List<IncomeStatementRowDto>> RowsAsync(IncomeStatementReportRequest request)
    {
        if (Validate(request) is { } error) throw new ApplicationException(error);

        var view = await StatementData.ViewAsync(db, request.ViewCode);
        var accounts = await StatementData.AccountsAsync(db);
        var reader = new AccountPostingReader(db);
        var filter = new AccountPostingFilter(request.BranchCode);
        var period = (await reader.ReadAsync(request.DateFrom, request.DateTo, filter)).Where(p => !p.ResultClosing).ToList();
        var yearStart = new DateTime(request.DateTo.Year, 1, 1);
        var ytd = (await reader.ReadAsync(yearStart, request.DateTo, filter)).Where(p => !p.ResultClosing).ToList();

        var periodValues = Compute(view.Lines, accounts, period);
        var ytdValues = Compute(view.Lines, accounts, ytd);
        var byCenter = request.ByCostCenter
            ? period.GroupBy(p => string.IsNullOrWhiteSpace(p.CostCenterCode) ? NoCenter : p.CostCenterCode!)
                .OrderBy(g => g.Key == NoCenter ? 1 : 0).ThenBy(g => g.Key, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => Compute(view.Lines, accounts, g.ToList()))
            : null;

        var ordered = view.Lines.OrderBy(l => l.Order).ThenBy(l => l.Code, StringComparer.Ordinal).ToList();
        var byCode = ordered.ToDictionary(l => l.Code, StringComparer.Ordinal);
        var revenueLine = ordered.FirstOrDefault(l => l.ParentCode is null && l.Kind != ManagementViewLineKind.Formula);
        var revenue = revenueLine is null ? 0m : periodValues.GetValueOrDefault(revenueLine.Code);

        return ordered.Where(l => !l.Hidden).Select(l =>
        {
            var value = periodValues.GetValueOrDefault(l.Code);
            return new IncomeStatementRowDto
            {
                Code = l.Code,
                Description = l.Description,
                Level = ManagementViewCalculator.Level(l, byCode),
                Bold = l.Bold || l.Kind != ManagementViewLineKind.Accounts,
                Period = value,
                YearToDate = ytdValues.GetValueOrDefault(l.Code),
                PercentOfRevenue = revenue == 0 ? null : Math.Round(value / revenue * 100m, 2, MidpointRounding.AwayFromZero),
                ByCostCenter = byCenter?.ToDictionary(c => c.Key, c => c.Value.GetValueOrDefault(l.Code)),
            };
        }).ToList();
    }

    private static Dictionary<string, decimal> Compute(IReadOnlyList<ManagementViewLine> lines,
        IReadOnlyDictionary<string, AccountInfo> accounts, IReadOnlyList<AccountPosting> postings)
    {
        var balances = postings.GroupBy(p => p.AccountCode).ToDictionary(g => g.Key, g => g.Sum(p => p.Debit - p.Credit));
        return ManagementViewCalculator.Compute(lines, accounts, code => balances.GetValueOrDefault(code));
    }

    public async Task<byte[]> ExecuteAsync(IncomeStatementReportRequest request)
    {
        var rows = await RowsAsync(request);
        var view = await StatementData.ViewAsync(db, request.ViewCode);
        var parameters = new Dictionary<string, object>
        {
            ["pTitle"] = view.Name,
            ["pPeriod"] = $"Período: {request.DateFrom:dd/MM/yyyy} a {request.DateTo:dd/MM/yyyy} — acumulado desde 01/01/{request.DateTo:yyyy}",
            ["pFilters"] = AccountingReportFilters.Describe(request.BranchCode, null, null, null, null),
        };
        return await reportService.GeneratePdfAsync("AccountingIncomeStatement.frx", rows, "DreRows", "DreRows", parameters);
    }
}

/// <summary>Leituras comuns da DRE e do Balanço.</summary>
public static class StatementData
{
    public static async Task<ManagementView> ViewAsync(IUnitOfWork db, string code) =>
        await db.Context.ManagementViews.AsNoTracking().Include(v => v.Lines)
            .FirstOrDefaultAsync(v => v.Code == code.Trim())
        ?? throw new ApplicationException($"Visão gerencial {code} não encontrada.");

    public static async Task<Dictionary<string, AccountInfo>> AccountsAsync(IUnitOfWork db) =>
        await db.Context.LedgerAccounts.AsNoTracking()
            .Select(a => new AccountInfo(a.Code, a.AllowsPosting, a.Nature, a.Type))
            .ToDictionaryAsync(a => a.Code);
}
