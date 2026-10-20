using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Infra.Queries;

/// <summary>
/// Posição financeira numa data-base (spec 2026-10-20, R1/R2/R4/R5). Função pura, sem acesso a banco: os relatórios
/// carregam os dados e passam aqui. Datas comparadas sem hora (C3).
/// </summary>
public static class FinancialPositionCalculator
{
    public static FinancialPosition Compute(FinancialPositionInput input, DateTime baseDate, FinancialPositionMode mode)
    {
        var baseDay = baseDate.Date;

        var documents = input.Documents
            .Where(d => d.Status != FinancialDocumentStatus.Canceled
                        && d.DocumentDate.Date <= baseDay
                        && IsIncluded(d, mode))
            .ToList();

        var byKey = documents.ToDictionary(d => d.Key);

        var settlements = input.Settlements
            .Where(s => byKey.ContainsKey(s.FinancialDocumentKey)
                        && s.SettlementDate.Date <= baseDay
                        && !((mode == FinancialPositionMode.Firm
                              || byKey[s.FinancialDocumentKey].Nature == FinancialDocumentNature.Provisional)
                             && IsCompositionOffset(s)))
            // Devolução: o ledger grava baixa positiva (reduz a devolução); aqui o valor dela é negativo, então a baixa também.
            .Select(s => new EffectiveSettlement(byKey[s.FinancialDocumentKey], s, s.SettlementDate.Date,
                IsReturn(byKey[s.FinancialDocumentKey]) ? -s.Amount : s.Amount, s.InterestAmount, s.FineAmount, s.DiscountAmount, false))
            .ToList();

        // Ajuste não-caixa do abatimento da devolução composta (R7): não é baixa, mas fecha o firme/devolução.
        if (mode == FinancialPositionMode.Firm)
            Distribute(input, baseDay, byKey, settlements);

        // R8: no modo Provisórios o valor do provisório é só o composto nele até a data-base (firmes − devoluções).
        Dictionary<Guid, decimal>? composedAtBase = null;
        if (mode == FinancialPositionMode.Provisional)
        {
            var eligible = input.Documents
                .Where(d => d.Status != FinancialDocumentStatus.Canceled && d.DocumentDate.Date <= baseDay)
                .ToDictionary(d => d.Key);
            composedAtBase = ComposedAtBase(input, baseDay, eligible)
                .ToDictionary(g => g.Key, g => g.Value.Sum(x => IsReturn(x.Document) ? -x.Amount : x.Amount));
            documents = documents
                .Where(d => d.Nature != FinancialDocumentNature.Provisional || composedAtBase.ContainsKey(d.Key)).ToList();
            var kept = documents.Select(d => d.Key).ToHashSet();
            settlements = settlements.Where(s => kept.Contains(s.Document.Key)).ToList();
        }

        var settledByDocument = settlements
            .GroupBy(s => s.Document.Key)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.Amount));

        var titles = documents.Select(d =>
        {
            var amount = composedAtBase is not null && d.Nature == FinancialDocumentNature.Provisional
                ? composedAtBase[d.Key]
                : IsReturn(d) ? -d.NetAmount : d.NetAmount;
            var settled = settledByDocument.GetValueOrDefault(d.Key);
            return new PositionTitle(d, amount, settled, amount - settled);
        }).ToList();

        return new FinancialPosition(titles, settlements);
    }

    /// <summary>
    /// Composto na data-base por provisório, reconstruído pelo ledger (InvoiceOffset/Reversal com CompositionKey no
    /// firme/devolução, apontando o provisório em RelatedDocumentKey, Date &lt;= base). Só entram os documentos de
    /// <paramref name="eligible"/> com saldo composto positivo. Valores positivos mesmo para devolução.
    /// </summary>
    private static Dictionary<Guid, List<(FinancialDocument Document, decimal Amount)>> ComposedAtBase(
        FinancialPositionInput input, DateTime baseDay, Dictionary<Guid, FinancialDocument> eligible) =>
        input.Settlements
            .Where(s => IsCompositionOffset(s) && s.RelatedDocumentKey is not null
                        && s.SettlementDate.Date <= baseDay && eligible.ContainsKey(s.FinancialDocumentKey))
            .GroupBy(s => s.RelatedDocumentKey!.Value)
            .Select(group => (group.Key, Composed: group.GroupBy(s => s.FinancialDocumentKey)
                .Select(g => (Document: eligible[g.Key], Amount: g.Sum(s => s.Amount)))
                .Where(x => x.Amount > 0m)
                .ToList()))
            .Where(x => x.Composed.Count > 0)
            .ToDictionary(x => x.Key, x => x.Composed);

    private sealed class Slot(FinancialDocument document, decimal capacity)
    {
        public FinancialDocument Document { get; } = document;
        public decimal Capacity { get; } = capacity; // composto na data-base
        public decimal Cash { get; set; }            // dinheiro do pool distribuído
        public decimal Netted { get; set; }          // abatido pela devolução composta
        public decimal Free => Capacity - Cash - Netted;
    }

    /// <summary>
    /// R5/R7/C1: o dinheiro baixado no provisório (Date &lt;= base, todas as origens, líquido de estorno) é distribuído
    /// aos firmes compostos nele na data-base, por vencimento/emissão/código. A composição é reconstruída pelo ledger
    /// (InvoiceOffset/Reversal com CompositionKey no firme), porque a linha de FINANCIAL_DOCUMENT_COMPOSITIONS some ao
    /// desfazer. A devolução composta fica em −D até o pool acumulado cobrir o líquido composto; nesse momento ela abate
    /// a capacidade restante dos firmes (do fim da fila) com fatias datadas pela baixa que atingiu o líquido.
    /// </summary>
    private static void Distribute(FinancialPositionInput input, DateTime baseDay,
        Dictionary<Guid, FinancialDocument> byKey, List<EffectiveSettlement> output)
    {
        var allDocs = input.Documents.ToDictionary(d => d.Key);

        foreach (var (provisionalKey, composed) in ComposedAtBase(input, baseDay, byKey))
        {
            if (!allDocs.TryGetValue(provisionalKey, out var provisional)
                || provisional.Nature != FinancialDocumentNature.Provisional
                || provisional.Status == FinancialDocumentStatus.Canceled)
                continue;

            var returns = composed.Where(x => IsReturn(x.Document)).Select(x => new Slot(x.Document, x.Amount)).ToList();
            var firms = composed.Where(x => !IsReturn(x.Document))
                .OrderBy(x => x.Document.DueDate.Date).ThenBy(x => x.Document.DocumentDate.Date)
                .ThenBy(x => x.Document.Code, StringComparer.Ordinal)
                .Select(x => new Slot(x.Document, x.Amount))
                .ToList();

            var returned = returns.Sum(x => x.Capacity);
            var net = firms.Sum(x => x.Capacity) - returned;
            var netted = false;

            // Líquido composto <= 0 (ex.: firmes compostos cancelados, sobrou só a devolução): não há o que distribuir —
            // firmes e devoluções ficam com o valor composto, sem abatimento (Math.Clamp lançaria com max < min).
            if (net <= 0m) continue;

            var pool = input.Settlements
                .Where(s => s.FinancialDocumentKey == provisional.Key && s.CompositionKey is null
                            && s.SettlementDate.Date <= baseDay)
                .OrderBy(s => s.SettlementDate.Date).ThenBy(s => s.Amount < 0m ? 1 : 0)
                .ToList();

            // Dinheiro distribuído = min(pool acumulado, líquido composto): o excedente fica no provisório, e o estorno
            // só desfaz o que o pool deixa de cobrir (não o que ainda está lá).
            var cum = new Dictionary<FinancialSettlement, decimal>(ReferenceEqualityComparer.Instance);
            var poolTotal = 0m;
            foreach (var payment in pool)
            {
                poolTotal += payment.Amount;
                var shouldNet = returned > 0m && poolTotal >= net;
                if (netted && !shouldNet)
                {
                    Net(payment, firms, returns, output, -1);
                    netted = false;
                }

                var diff = Math.Clamp(poolTotal, 0m, net) - firms.Sum(x => x.Cash);
                if (diff > 0m) Fill(payment, diff, firms, output, cum);
                else if (diff < 0m) Unwind(payment, -diff, firms, output, cum);

                if (!netted && shouldNet)
                {
                    Net(payment, firms, returns, output, +1);
                    netted = true;
                }
            }
        }
    }

    // Abatimento da devolução (sign = +1) ou o seu desfazimento (sign = -1, no estorno que derruba o pool abaixo do líquido).
    private static void Net(FinancialSettlement source, List<Slot> firms, List<Slot> returns,
        List<EffectiveSettlement> output, int sign)
    {
        if (sign > 0)
        {
            var remaining = returns.Sum(x => x.Capacity);
            for (var i = firms.Count - 1; i >= 0 && remaining > 0m; i--)
            {
                var piece = Math.Min(firms[i].Free, remaining);
                if (piece <= 0m) continue;
                firms[i].Netted += piece;
                remaining -= piece;
                output.Add(Slice(firms[i].Document, source, piece, null, true));
            }
        }
        else
        {
            foreach (var firm in firms.Where(x => x.Netted > 0m))
            {
                output.Add(Slice(firm.Document, source, -firm.Netted, null, true));
                firm.Netted = 0m;
            }
        }

        foreach (var ret in returns)
        {
            ret.Netted = sign > 0 ? ret.Capacity : 0m;
            output.Add(Slice(ret.Document, source, -sign * ret.Capacity, null, true));
        }
    }

    private static void Fill(FinancialSettlement payment, decimal amount, List<Slot> slots, List<EffectiveSettlement> output,
        Dictionary<FinancialSettlement, decimal> cum)
    {
        var remaining = amount;
        foreach (var slot in slots)
        {
            if (remaining <= 0m) break;
            var piece = Math.Min(slot.Free, remaining);
            if (piece <= 0m) continue;
            slot.Cash += piece;
            remaining -= piece;
            output.Add(Slice(slot.Document, payment, piece, cum));
        }
    }

    private static void Unwind(FinancialSettlement reversal, decimal amount, List<Slot> slots, List<EffectiveSettlement> output,
        Dictionary<FinancialSettlement, decimal> cum)
    {
        var remaining = amount;
        for (var i = slots.Count - 1; i >= 0 && remaining > 0m; i--)
        {
            var piece = Math.Min(slots[i].Cash, remaining);
            if (piece <= 0m) continue;
            slots[i].Cash -= piece;
            remaining -= piece;
            output.Add(Slice(slots[i].Document, reversal, -piece, cum));
        }
    }

    // Juros/multa/desconto rateados pela proporção do pedaço na baixa real.
    // Abatimento da devolução não é dinheiro: sem juros/multa/desconto. Dinheiro: juros/multa/desconto rateados pela
    // proporção ACUMULADA do pedaço na baixa real (round(acum) − round(acum anterior)), para as fatias somarem a linha.
    private static EffectiveSettlement Slice(FinancialDocument document, FinancialSettlement source, decimal piece,
        Dictionary<FinancialSettlement, decimal>? cum, bool isReturnNetting = false)
    {
        if (cum is null || source.Amount == 0m)
            return new EffectiveSettlement(document, source, source.SettlementDate.Date, piece, 0m, 0m, 0m, true, isReturnNetting);

        var before = cum.GetValueOrDefault(source);
        var after = before + Math.Abs(piece);
        cum[source] = after;
        var sign = Math.Sign(piece) * Math.Sign(source.Amount);
        var total = Math.Abs(source.Amount);
        decimal Share(decimal value) => sign * (decimal.Round(value * after / total, 2) - decimal.Round(value * before / total, 2));
        return new EffectiveSettlement(document, source, source.SettlementDate.Date, piece,
            Share(source.InterestAmount), Share(source.FineAmount), Share(source.DiscountAmount), true);
    }

    // Firmes: tudo menos o provisório. Provisórios: o provisório entra NO LUGAR dos firmes/devoluções de contrato.
    private static bool IsIncluded(FinancialDocument d, FinancialPositionMode mode) => mode == FinancialPositionMode.Firm
        ? d.Nature != FinancialDocumentNature.Provisional
        : !d.IsContractFirmDocument;

    private static bool IsReturn(FinancialDocument d) =>
        d.Nature is FinancialDocumentNature.PurchaseReturn or FinancialDocumentNature.SalesReturn;

    // R5: o abatimento por composição e o seu estorno (que herda a CompositionKey) não são baixa no modo Firmes.
    private static bool IsCompositionOffset(FinancialSettlement s) =>
        s.CompositionKey is not null
        && s.Origin is FinancialSettlementOrigin.InvoiceOffset or FinancialSettlementOrigin.Reversal;
}
