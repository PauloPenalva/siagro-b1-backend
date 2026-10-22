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
        IReadOnlyDictionary<string, AccountInfo> accounts, Func<string, decimal> signedBalanceOfAnalytic,
        IReadOnlyDictionary<string, decimal>? fixedValues = null)
    {
        var byCode = lines.ToDictionary(l => l.Code, StringComparer.Ordinal);
        var values = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        decimal Value(string code)
        {
            if (values.TryGetValue(code, out var known)) return known;
            if (fixedValues is not null && fixedValues.TryGetValue(code, out var fixedValue)) return values[code] = fixedValue;
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
            var inRange = accounts.Values
                .Where(a => a.Analytic && string.CompareOrdinal(a.Code, from) >= 0 && string.CompareOrdinal(a.Code, to) <= 0)
                .OrderBy(a => a.Code, StringComparer.Ordinal).ToList();
            if (inRange.Count == 0) return 0m;

            // O sentido é o da LINHA, não o de cada conta: a redutora da faixa (devoluções na receita, depreciação no
            // ativo) tem natureza oposta e precisa subtrair. Vale a conta "de" da faixa (em geral a sintética do
            // grupo); se ela não existir, a primeira analítica da faixa.
            var head = accounts.GetValueOrDefault(from) ?? inRange[0];
            return inRange.Sum(a => signedBalanceOfAnalytic(a.Code)) * NaturalFactor(head);
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
