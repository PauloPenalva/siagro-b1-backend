using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services.Accounting;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Balanço Patrimonial (CTBR500) pela visão gerencial, na data-base, com comparativo opcional. Saldos cumulativos até
/// a data, incluindo a apuração. Enquanto houver resultado não apurado, ele entra como linha calculada sob o segundo
/// grupo de nível 1 (Passivo e PL), para o Balanço fechar.
/// </summary>
public class BalanceSheetReportService(IUnitOfWork db, IFastReportService reportService)
{
    public const string UnclosedResultCode = "__RESULT";
    public const string UnclosedResultText = "Resultado do período não apurado";

    public static string? Validate(BalanceSheetReportRequest request) =>
        string.IsNullOrWhiteSpace(request.ViewCode) ? "Escolha a visão gerencial." : null;

    private sealed record Snapshot(Dictionary<string, decimal> Values, decimal Unclosed);

    public async Task<List<BalanceSheetRowDto>> RowsAsync(BalanceSheetReportRequest request)
    {
        if (Validate(request) is { } error) throw new ApplicationException(error);

        var view = await StatementData.ViewAsync(db, request.ViewCode);
        var accounts = await StatementData.AccountsAsync(db);
        var ordered = view.Lines.OrderBy(l => l.Order).ThenBy(l => l.Code, StringComparer.Ordinal).ToList();
        var byCode = ordered.ToDictionary(l => l.Code, StringComparer.Ordinal);
        var group = ordered.Where(l => l.ParentCode is null && l.Kind == ManagementViewLineKind.Total).Skip(1).FirstOrDefault();

        var main = await SnapshotAsync(view.Lines, accounts, request.Date, request.BranchCode, group);
        var compare = request.CompareDate is { } c
            ? await SnapshotAsync(view.Lines, accounts, c, request.BranchCode, group)
            : null;

        var rows = new List<BalanceSheetRowDto>();
        foreach (var line in ordered)
        {
            if (!line.Hidden)
                rows.Add(new BalanceSheetRowDto
                {
                    Code = line.Code,
                    Description = line.Description,
                    Level = ManagementViewCalculator.Level(line, byCode),
                    Bold = line.Bold || line.Kind != ManagementViewLineKind.Accounts,
                    Balance = main.Values.GetValueOrDefault(line.Code),
                    CompareBalance = compare?.Values.GetValueOrDefault(line.Code),
                });

            // A linha calculada fica depois da última descendente do grupo do passivo.
            if (group is not null && (main.Unclosed != 0 || (compare?.Unclosed ?? 0) != 0) &&
                IsLastOfGroup(line, group, ordered, byCode))
                rows.Add(new BalanceSheetRowDto
                {
                    Code = UnclosedResultCode,
                    Description = UnclosedResultText,
                    Level = ManagementViewCalculator.Level(group, byCode) + 1,
                    Balance = main.Unclosed,
                    CompareBalance = compare?.Unclosed,
                });
        }

        return rows;
    }

    private static bool IsLastOfGroup(ManagementViewLine line, ManagementViewLine group, List<ManagementViewLine> ordered,
        IReadOnlyDictionary<string, ManagementViewLine> byCode)
    {
        bool InGroup(ManagementViewLine l)
        {
            var seen = new HashSet<string>();
            for (var current = l; current is not null && seen.Add(current.Code);
                 current = current.ParentCode is { } p ? byCode.GetValueOrDefault(p) : null)
                if (current.Code == group.Code) return true;
            return false;
        }

        return InGroup(line) && ordered.Last(InGroup) == line;
    }

    private async Task<Snapshot> SnapshotAsync(IReadOnlyList<ManagementViewLine> lines,
        IReadOnlyDictionary<string, AccountInfo> accounts, DateTime date, string? branch, ManagementViewLine? group)
    {
        var postings = await new AccountPostingReader(db).ReadAsync(null, date, new AccountPostingFilter(branch));
        var balances = postings.GroupBy(p => p.AccountCode).ToDictionary(g => g.Key, g => g.Sum(p => p.Debit - p.Credit));

        // Lucro positivo: o saldo das contas de resultado com sinal credor.
        var unclosed = -accounts.Values
            .Where(a => a.Analytic && a.Type is LedgerAccountType.Revenue or LedgerAccountType.Expense)
            .Sum(a => balances.GetValueOrDefault(a.Code));

        // Entra como filha calculada do grupo do passivo, para chegar também aos Totais e Fórmulas acima dele.
        var withResult = group is null
            ? lines
            : lines.Append(new ManagementViewLine
            {
                Code = UnclosedResultCode, Description = UnclosedResultText, ParentCode = group.Code,
                Kind = ManagementViewLineKind.Formula, Sign = ManagementViewLineSign.Plus,
            }).ToList();
        var values = ManagementViewCalculator.Compute(withResult, accounts, code => balances.GetValueOrDefault(code),
            new Dictionary<string, decimal> { [UnclosedResultCode] = unclosed });
        return new Snapshot(values, unclosed);
    }

    public async Task<byte[]> ExecuteAsync(BalanceSheetReportRequest request)
    {
        var rows = await RowsAsync(request);
        var view = await StatementData.ViewAsync(db, request.ViewCode);
        var parameters = new Dictionary<string, object>
        {
            ["pTitle"] = view.Name,
            ["pPeriod"] = request.CompareDate is { } c
                ? $"Data-base: {request.Date:dd/MM/yyyy} — comparativo: {c:dd/MM/yyyy}"
                : $"Data-base: {request.Date:dd/MM/yyyy}",
            ["pFilters"] = AccountingReportFilters.Describe(request.BranchCode, null, null, null, null),
            ["pDate"] = request.Date.ToString("dd/MM/yyyy"),
            ["pCompareDate"] = request.CompareDate?.ToString("dd/MM/yyyy") ?? string.Empty,
            ["pHasCompare"] = request.CompareDate is not null,
        };
        return await reportService.GeneratePdfAsync("AccountingBalanceSheet.frx", rows, "BpRows", "BpRows", parameters);
    }
}
