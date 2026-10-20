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
                        && !(mode == FinancialPositionMode.Firm && IsCompositionOffset(s)))
            .Select(s => new EffectiveSettlement(byKey[s.FinancialDocumentKey], s, s.SettlementDate.Date,
                s.Amount, s.InterestAmount, s.FineAmount, s.DiscountAmount, false))
            .ToList();

        // Ajuste não-caixa do abatimento da devolução composta (R7): não é baixa, mas fecha o firme/devolução.
        var netting = new Dictionary<Guid, decimal>();
        if (mode == FinancialPositionMode.Firm)
            Distribute(input, baseDay, byKey, settlements, netting);

        var settledByDocument = settlements
            .GroupBy(s => s.Document.Key)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.Amount));
        foreach (var (key, value) in netting)
            settledByDocument[key] = settledByDocument.GetValueOrDefault(key) + value;

        var titles = documents.Select(d =>
        {
            var amount = IsReturn(d) ? -d.NetAmount : d.NetAmount;
            var settled = settledByDocument.GetValueOrDefault(d.Key);
            return new PositionTitle(d, amount, settled, amount - settled);
        }).ToList();

        return new FinancialPosition(titles, settlements);
    }

    private sealed class Slot(FinancialDocument document, decimal capacity, decimal reduction)
    {
        public FinancialDocument Document { get; } = document;
        public decimal Capacity { get; } = capacity;   // composto na data-base, já abatida a devolução
        public decimal Reduction { get; } = reduction; // parte do composto absorvida pela devolução
        public decimal Filled { get; set; }
    }

    /// <summary>
    /// R5/R7/C1: o dinheiro baixado no provisório (Date &lt;= base, todas as origens, líquido de estorno) é distribuído
    /// aos firmes compostos nele na data-base, por vencimento/emissão/código. A composição é reconstruída pelo ledger
    /// (InvoiceOffset/Reversal com CompositionKey no firme), porque a linha de FINANCIAL_DOCUMENT_COMPOSITIONS some ao
    /// desfazer. A devolução composta reduz primeiro a capacidade dos firmes e sai quando o pool cobre o líquido.
    /// </summary>
    private static void Distribute(FinancialPositionInput input, DateTime baseDay,
        Dictionary<Guid, FinancialDocument> byKey, List<EffectiveSettlement> output, Dictionary<Guid, decimal> netting)
    {
        var allDocs = input.Documents.ToDictionary(d => d.Key);

        var composedLines = input.Settlements
            .Where(s => IsCompositionOffset(s) && s.RelatedDocumentKey is not null
                        && s.SettlementDate.Date <= baseDay && byKey.ContainsKey(s.FinancialDocumentKey))
            .GroupBy(s => s.RelatedDocumentKey!.Value);

        foreach (var group in composedLines)
        {
            if (!allDocs.TryGetValue(group.Key, out var provisional)
                || provisional.Nature != FinancialDocumentNature.Provisional
                || provisional.Status == FinancialDocumentStatus.Canceled)
                continue;

            var composed = group.GroupBy(s => s.FinancialDocumentKey)
                .Select(g => (Document: byKey[g.Key], Amount: g.Sum(s => s.Amount)))
                .Where(x => x.Amount > 0m)
                .ToList();

            var returns = composed.Where(x => IsReturn(x.Document)).ToList();
            var firms = composed.Where(x => !IsReturn(x.Document))
                .OrderBy(x => x.Document.DueDate.Date).ThenBy(x => x.Document.DocumentDate.Date)
                .ThenBy(x => x.Document.Code, StringComparer.Ordinal)
                .ToList();

            var toNet = returns.Sum(x => x.Amount);
            var slots = new List<Slot>();
            foreach (var (document, amount) in firms)
            {
                var reduction = Math.Min(amount, toNet);
                toNet -= reduction;
                slots.Add(new Slot(document, amount - reduction, reduction));
            }

            var pool = input.Settlements
                .Where(s => s.FinancialDocumentKey == provisional.Key && s.CompositionKey is null
                            && s.SettlementDate.Date <= baseDay)
                .OrderBy(s => s.SettlementDate.Date).ThenBy(s => s.Amount < 0m ? 1 : 0)
                .ToList();

            foreach (var payment in pool)
            {
                if (payment.Amount > 0m) Fill(payment, slots, output);
                else if (payment.Amount < 0m) Unwind(payment, slots, output);
            }

            // Firme coberto pelo dinheiro: a parte absorvida pela devolução também o fecha.
            foreach (var slot in slots.Where(x => x.Reduction > 0m && x.Filled >= x.Capacity))
                netting[slot.Document.Key] = netting.GetValueOrDefault(slot.Document.Key) + slot.Reduction;

            var net = firms.Sum(x => x.Amount) - returns.Sum(x => x.Amount);
            if (returns.Count > 0 && slots.Sum(x => x.Filled) >= net)
                foreach (var (document, amount) in returns)
                    netting[document.Key] = netting.GetValueOrDefault(document.Key) - amount;
        }
    }

    private static void Fill(FinancialSettlement payment, List<Slot> slots, List<EffectiveSettlement> output)
    {
        var remaining = payment.Amount;
        foreach (var slot in slots)
        {
            if (remaining <= 0m) break;
            var piece = Math.Min(slot.Capacity - slot.Filled, remaining);
            if (piece <= 0m) continue;
            slot.Filled += piece;
            remaining -= piece;
            output.Add(Slice(slot.Document, payment, piece));
        }
    }

    private static void Unwind(FinancialSettlement reversal, List<Slot> slots, List<EffectiveSettlement> output)
    {
        var remaining = -reversal.Amount;
        for (var i = slots.Count - 1; i >= 0 && remaining > 0m; i--)
        {
            var piece = Math.Min(slots[i].Filled, remaining);
            if (piece <= 0m) continue;
            slots[i].Filled -= piece;
            remaining -= piece;
            output.Add(Slice(slots[i].Document, reversal, -piece));
        }
    }

    // Juros/multa/desconto rateados pela proporção do pedaço na baixa real.
    private static EffectiveSettlement Slice(FinancialDocument document, FinancialSettlement source, decimal piece)
    {
        var ratio = source.Amount == 0m ? 0m : piece / source.Amount;
        return new EffectiveSettlement(document, source, source.SettlementDate.Date, piece,
            decimal.Round(source.InterestAmount * ratio, 2), decimal.Round(source.FineAmount * ratio, 2),
            decimal.Round(source.DiscountAmount * ratio, 2), true);
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
