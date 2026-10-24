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

    /// <summary>Contas de resultado (receita e despesa), as do I355/K355 e da apuração.</summary>
    public static HashSet<string> ResultAccounts(EcdData data) =>
        data.Accounts.Where(a => a.Type is LedgerAccountType.Revenue or LedgerAccountType.Expense).Select(a => a.Code).ToHashSet();

    /// <summary>
    /// Plano de contas do arquivo (I050 da ECD, J050 da ECF): analíticas com saldo ou movimento e as superiores delas.
    /// </summary>
    public static SortedSet<string> Plan(EcdData data, List<EcdPosting> postings)
    {
        var accounts = data.Accounts.ToDictionary(a => a.Code);
        var plan = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var code in UsedAccounts(data, postings))
            for (var c = accounts.GetValueOrDefault(code); c != null && plan.Add(c.Code);
                 c = c.ParentCode == null ? null : accounts.GetValueOrDefault(c.ParentCode)) { }
        return plan;
    }

    /// <summary>Centros de custo com saldo ou movimento nos meses (I100 da ECD, J100 da ECF).</summary>
    public static List<string> UsedCostCenters(List<List<EcdMonthBalance>> months) =>
        months.SelectMany(m => m).Select(r => r.CostCenter).OfType<string>().Distinct().Order(StringComparer.Ordinal).ToList();

    /// <summary>
    /// Saldo das contas de resultado antes da apuração (I355 da ECD, K355 da ECF): abertura opcional + partidas dadas,
    /// por conta e centro; só os não zerados, em ordem de conta e centro.
    /// </summary>
    public static List<KeyValuePair<(string Account, string? CostCenter), decimal>> ResultBalances(EcdData data,
        IEnumerable<EcdPosting> postings, bool includeOpening = true)
    {
        var result = ResultAccounts(data);
        var balances = includeOpening
            ? Opening(data).Where(kv => result.Contains(kv.Key.Account)).ToDictionary(kv => kv.Key, kv => kv.Value)
            : new Dictionary<(string Account, string? CostCenter), decimal>();
        foreach (var p in postings.Where(p => result.Contains(p.Account)))
            balances[(p.Account, p.CostCenter)] = balances.GetValueOrDefault((p.Account, p.CostCenter)) + p.Debit - p.Credit;
        return balances.Where(kv => kv.Value != 0)
            .OrderBy(kv => kv.Key.Account, StringComparer.Ordinal).ThenBy(kv => kv.Key.CostCenter ?? "", StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>D/C do saldo (débito − crédito); zerado sai pelo sentido natural da conta.</summary>
    public static string Side(decimal signed, AccountInfo account) =>
        signed > 0 ? "D" : signed < 0 ? "C" : ManagementViewCalculator.NaturalFactor(account) > 0 ? "D" : "C";

    /// <summary>Natureza do grupo (COD_NAT do I050/J050).</summary>
    public static string GroupNature(LedgerAccountType? type) => type switch
    {
        LedgerAccountType.Asset => "01",
        LedgerAccountType.Liability => "02",
        LedgerAccountType.Equity => "03",
        _ => "04",
    };

    /// <summary>Nível da conta no plano (1 = sem superior).</summary>
    public static int Depth(LedgerAccount account, Dictionary<string, LedgerAccount> accounts)
    {
        var depth = 1;
        var seen = new HashSet<string> { account.Code };
        for (var p = account.ParentCode; p != null && seen.Add(p) && accounts.TryGetValue(p, out var parent); p = parent.ParentCode)
            depth++;
        return depth;
    }

    /// <summary>Resultado do ano pela DRE: contas de resultado sem a apuração (lucro positivo).</summary>
    public static decimal NetIncome(EcdData data, List<EcdPosting> postings)
    {
        var result = ResultAccounts(data);
        return -postings.Where(p => !p.ResultClosing && result.Contains(p.Account)).Sum(p => p.Debit - p.Credit);
    }
}
