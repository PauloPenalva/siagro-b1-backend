using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services.Accounting;

namespace SiagroB1.Reports.Services.Ecd;

/// <summary>Uma partida (lado débito ou crédito) de uma linha de lançamento do ano.</summary>
public sealed record EcdPosting(JournalEntry Entry, string Account, string? CostCenter, decimal Debit, decimal Credit,
    string History)
{
    public bool ResultClosing => Entry.SourceDocumentType == AccountPostingReader.ResultClosingType;
}

/// <summary>Saldo de uma conta analítica + centro de custo num mês (I155).</summary>
public sealed record EcdMonthBalance(string Account, string? CostCenter, decimal Opening, decimal Debit, decimal Credit)
{
    public decimal Closing => Opening + Debit - Credit;
}

/// <summary>
/// Contas do ano lidas da carga da ECD com as mesmas regras do Razão/Balancete (<see cref="AccountPostingReader"/>):
/// linha tipo 3 vira duas partidas, tipo 4 só continua o histórico. Chave de saldo = (conta, centro de custo do
/// lado), com centro vazio normalizado para nulo, igual ao saldo de abertura.
/// </summary>
public static class EcdLedger
{
    public static string? CostCenter(string? code) => string.IsNullOrWhiteSpace(code) ? null : code.Trim();

    public static List<EcdPosting> Postings(EcdData data)
    {
        var postings = new List<EcdPosting>();
        foreach (var e in data.Entries)
        {
            var lines = e.Lines.OrderBy(l => l.LineNumber).ToList();
            for (var i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                if (l.EntryType == JournalEntryLineType.HistoryContinuation) continue;
                var history = l.History;
                for (var j = i + 1; j < lines.Count && lines[j].EntryType == JournalEntryLineType.HistoryContinuation; j++)
                    history += lines[j].History;

                if (l.DebitAccountCode is not null)
                    postings.Add(new EcdPosting(e, l.DebitAccountCode, CostCenter(l.DebitCostCenterCode), l.Amount, 0m, history));
                if (l.CreditAccountCode is not null)
                    postings.Add(new EcdPosting(e, l.CreditAccountCode, CostCenter(l.CreditCostCenterCode), 0m, l.Amount, history));
            }
        }

        return postings;
    }

    /// <summary>Saldo de abertura com a chave normalizada (centro vazio = nulo).</summary>
    public static Dictionary<(string Account, string? CostCenter), decimal> Opening(EcdData data) =>
        data.OpeningBalances
            .GroupBy(kv => (kv.Key.Account, CostCenter: CostCenter(kv.Key.CostCenter)))
            .ToDictionary(g => g.Key, g => g.Sum(kv => kv.Value));

    /// <summary>I155 dos 12 meses: só chaves com saldo inicial ou movimento no mês.</summary>
    public static List<List<EcdMonthBalance>> Months(EcdData data, List<EcdPosting> postings)
    {
        var running = Opening(data);
        var months = new List<List<EcdMonthBalance>>();
        for (var month = 1; month <= 12; month++)
        {
            var moves = postings.Where(p => p.Entry.PostingDate.Month == month)
                .GroupBy(p => (p.Account, p.CostCenter))
                .ToDictionary(g => g.Key, g => (Debit: g.Sum(p => p.Debit), Credit: g.Sum(p => p.Credit)));
            var keys = running.Where(kv => kv.Value != 0).Select(kv => kv.Key).Union(moves.Keys)
                .OrderBy(k => k.Account, StringComparer.Ordinal).ThenBy(k => k.CostCenter ?? "", StringComparer.Ordinal)
                .ToList();
            var rows = keys.Select(k =>
            {
                var m = moves.GetValueOrDefault(k);
                return new EcdMonthBalance(k.Account, k.CostCenter, running.GetValueOrDefault(k), m.Debit, m.Credit);
            }).ToList();
            foreach (var r in rows) running[(r.Account, r.CostCenter)] = r.Closing;
            months.Add(rows);
        }

        return months;
    }

    /// <summary>Contas analíticas que entram no arquivo: com saldo de abertura ou movimento no ano.</summary>
    public static SortedSet<string> UsedAccounts(EcdData data, List<EcdPosting> postings) =>
        new(Opening(data).Where(kv => kv.Value != 0).Select(kv => kv.Key.Account).Concat(postings.Select(p => p.Account)),
            StringComparer.Ordinal);

    public static Dictionary<string, AccountInfo> AccountInfos(EcdData data) =>
        data.Accounts.ToDictionary(a => a.Code, a => new AccountInfo(a.Code, a.AllowsPosting, a.Nature, a.Type));

    /// <summary>
    /// Linha de tipo Contas da visão cuja faixa contém a analítica (mesma regra de faixa do
    /// <see cref="ManagementViewCalculator"/>); a primeira pela ordem da visão.
    /// </summary>
    public static ManagementViewLine? LineOf(ManagementView? view, string account) =>
        view?.Lines.OrderBy(l => l.Order).ThenBy(l => l.Code, StringComparer.Ordinal)
            .FirstOrDefault(l => l.Kind == ManagementViewLineKind.Accounts
                                 && !string.IsNullOrWhiteSpace(l.AccountFrom) && !string.IsNullOrWhiteSpace(l.AccountTo)
                                 && string.CompareOrdinal(account, l.AccountFrom.Trim()) >= 0
                                 && string.CompareOrdinal(account, l.AccountTo.Trim()) <= 0);

    /// <summary>Saldo (débito − crédito) por conta: abertura + partidas filtradas.</summary>
    public static Dictionary<string, decimal> Balances(EcdData data, IEnumerable<EcdPosting> postings)
    {
        var balances = Opening(data).GroupBy(kv => kv.Key.Account).ToDictionary(g => g.Key, g => g.Sum(kv => kv.Value));
        foreach (var p in postings)
            balances[p.Account] = balances.GetValueOrDefault(p.Account) + p.Debit - p.Credit;
        return balances;
    }

    /// <summary>Resultado do ano pela DRE: contas de resultado sem a apuração (lucro positivo).</summary>
    public static decimal NetIncome(EcdData data, List<EcdPosting> postings)
    {
        var result = data.Accounts.Where(a => a.Type is LedgerAccountType.Revenue or LedgerAccountType.Expense)
            .Select(a => a.Code).ToHashSet();
        return -postings.Where(p => !p.ResultClosing && result.Contains(p.Account)).Sum(p => p.Debit - p.Credit);
    }
}
