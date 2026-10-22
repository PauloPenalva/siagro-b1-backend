using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Reports.Services.Accounting;

/// <summary>Dados da conta que o calculador precisa.</summary>
public sealed record AccountInfo(string Code, bool Analytic, LedgerAccountNature? Nature, LedgerAccountType? Type);

/// <summary>
/// Calcula as linhas da visão gerencial (CTS). Contas = saldo das analíticas da faixa no sentido natural da conta
/// (receita e passivo positivos quando credores; ativo e despesa positivos quando devedores). Total = soma das filhas,
/// cada uma com o seu Sinal (Minus subtrai). Fórmula = soma/subtração dos valores das linhas citadas.
/// </summary>
public static class ManagementViewCalculator
{
    public static Dictionary<string, decimal> Compute(IReadOnlyList<ManagementViewLine> lines,
        IReadOnlyDictionary<string, AccountInfo> accounts, Func<string, decimal> signedBalanceOfAnalytic)
    {
        var byCode = lines.ToDictionary(l => l.Code, StringComparer.Ordinal);
        var values = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        decimal Value(string code)
        {
            if (values.TryGetValue(code, out var known)) return known;
            if (!byCode.TryGetValue(code, out var line)) return 0m;
            if (!visiting.Add(code)) return 0m; // ciclo legado: o cadastro já recusa, aqui só não estoura a pilha

            var value = line.Kind switch
            {
                ManagementViewLineKind.Accounts => AccountsValue(line),
                ManagementViewLineKind.Total => lines.Where(l => l.ParentCode == code)
                    .Sum(l => Value(l.Code) * (l.Sign == ManagementViewLineSign.Minus ? -1 : 1)),
                ManagementViewLineKind.Formula => ManagementViewFormula.Parse(line.Formula ?? string.Empty)
                    .Sum(t => t.Sign * Value(t.Code)),
                _ => 0m,
            };
            visiting.Remove(code);
            values[code] = value;
            return value;
        }

        decimal AccountsValue(ManagementViewLine line)
        {
            if (string.IsNullOrWhiteSpace(line.AccountFrom) || string.IsNullOrWhiteSpace(line.AccountTo)) return 0m;
            var from = line.AccountFrom.Trim();
            var to = line.AccountTo.Trim();
            return accounts.Values
                .Where(a => a.Analytic && string.CompareOrdinal(a.Code, from) >= 0 && string.CompareOrdinal(a.Code, to) <= 0)
                .Sum(a => signedBalanceOfAnalytic(a.Code) * NaturalFactor(a));
        }

        foreach (var line in lines) Value(line.Code);
        return values;
    }

    /// <summary>+1 para conta devedora, −1 para credora; sem natureza, pelo tipo (Ativo/Despesa devedores).</summary>
    public static int NaturalFactor(AccountInfo a) => a.Nature switch
    {
        LedgerAccountNature.Debit => 1,
        LedgerAccountNature.Credit => -1,
        _ => a.Type is LedgerAccountType.Asset or LedgerAccountType.Expense ? 1 : -1,
    };

    /// <summary>Profundidade da linha pela linha superior (1 = sem superior).</summary>
    public static int Level(ManagementViewLine line, IReadOnlyDictionary<string, ManagementViewLine> byCode)
    {
        var depth = 1;
        var seen = new HashSet<string> { line.Code };
        for (var parent = line.ParentCode; parent is not null && seen.Add(parent) && byCode.TryGetValue(parent, out var p);
             parent = p.ParentCode)
            depth++;
        return depth;
    }
}
