using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Application.Services.PurchaseFiscalReconciliations;

public sealed record PlannerLine(Guid Key, PurchaseInvoiceType InvoiceType, DateTime? IssueDate, string Number, decimal Available);

/// <summary><c>OpenBalance</c> = físico − fiscal já vinculado (pode ser negativo).</summary>
public sealed record PlannerTransaction(Guid Key, StorageTransactionType Type, DateTime? Date, string Code, decimal OpenBalance);

public sealed record PlannerPair(Guid LineKey, Guid TransactionKey, decimal Quantity);

/// <summary>
/// Distribuição sugerida do diálogo "Vincular" (spec 2026-10-13 §6.2). NFs por emissão, romaneios por data; cada NF
/// preenche o saldo aberto dos romaneios na ordem; o que sobrar da NF depois do último romaneio vai para ele (FOB: NF
/// no peso de origem maior que a balança). Pares de sentido inválido nunca são sugeridos. Só sugere — o usuário edita.
/// </summary>
public static class PurchaseFiscalReconciliationPlanner
{
    public static List<PlannerPair> Suggest(IReadOnlyList<PlannerLine> lines, IReadOnlyList<PlannerTransaction> transactions)
    {
        var open = transactions.ToDictionary(t => t.Key, t => Math.Max(t.OpenBalance, 0m));
        var orderedTransactions = transactions
            .OrderBy(t => t.Date).ThenBy(t => t.Code, StringComparer.Ordinal).ToList();
        var result = new List<PlannerPair>();

        foreach (var line in lines.OrderBy(l => l.IssueDate).ThenBy(l => l.Number, StringComparer.Ordinal))
        {
            var eligible = orderedTransactions
                .Where(t => PurchaseFiscalReconciliationRules.SenseMatches(line.InvoiceType, t.Type)).ToList();
            var remaining = line.Available;
            if (remaining <= 0 || eligible.Count == 0)
                continue;

            var assigned = new Dictionary<Guid, decimal>();
            foreach (var tx in eligible)
            {
                if (remaining <= 0) break;
                var take = Math.Min(open[tx.Key], remaining);
                if (take <= 0) continue;
                assigned[tx.Key] = take;
                open[tx.Key] -= take;
                remaining -= take;
            }

            if (remaining > 0)
            {
                var last = eligible[^1].Key;
                assigned[last] = assigned.GetValueOrDefault(last) + remaining;
            }

            result.AddRange(eligible.Where(t => assigned.ContainsKey(t.Key))
                .Select(t => new PlannerPair(line.Key, t.Key, assigned[t.Key])));
        }

        return result;
    }
}
