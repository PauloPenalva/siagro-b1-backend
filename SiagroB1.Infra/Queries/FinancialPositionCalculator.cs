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
        if (mode == FinancialPositionMode.Firm)
            Distribute(input, baseDay, byKey, settlements);

        var settledByDocument = settlements
            .GroupBy(s => s.Document.Key)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.Amount));

        var titles = documents.Select(d =>
        {
            var amount = IsReturn(d) ? -d.NetAmount : d.NetAmount;
            var settled = settledByDocument.GetValueOrDefault(d.Key);
            return new PositionTitle(d, amount, settled, amount - settled);
        }).ToList();

        return new FinancialPosition(titles, settlements);
    }

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

            var returns = composed.Where(x => IsReturn(x.Document)).Select(x => new Slot(x.Document, x.Amount)).ToList();
            var firms = composed.Where(x => !IsReturn(x.Document))
                .OrderBy(x => x.Document.DueDate.Date).ThenBy(x => x.Document.DocumentDate.Date)
                .ThenBy(x => x.Document.Code, StringComparer.Ordinal)
                .Select(x => new Slot(x.Document, x.Amount))
                .ToList();

            var returned = returns.Sum(x => x.Capacity);
            var net = firms.Sum(x => x.Capacity) - returned;
            var netted = false;

            var pool = input.Settlements
                .Where(s => s.FinancialDocumentKey == provisional.Key && s.CompositionKey is null
                            && s.SettlementDate.Date <= baseDay)
                .OrderBy(s => s.SettlementDate.Date).ThenBy(s => s.Amount < 0m ? 1 : 0)
                .ToList();

            foreach (var payment in pool)
            {
                if (payment.Amount > 0m)
                {
                    // Antes do abatimento o dinheiro não passa do líquido: o resto da capacidade é da devolução.
                    var cash = firms.Sum(x => x.Cash);
                    var amount = netted || returned == 0m ? payment.Amount : Math.Min(payment.Amount, net - cash);
                    Fill(payment, amount, firms, output);
                    if (!netted && returned > 0m && firms.Sum(x => x.Cash) >= net)
                    {
                        Net(payment, firms, returns, output, +1);
                        netted = true;
                    }
                }
                else if (payment.Amount < 0m)
                {
                    if (netted && firms.Sum(x => x.Cash) + payment.Amount < net)
                    {
                        Net(payment, firms, returns, output, -1);
                        netted = false;
                    }
                    Unwind(payment, firms, output);
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
                output.Add(Slice(firms[i].Document, source, piece, false));
            }
        }
        else
        {
            foreach (var firm in firms.Where(x => x.Netted > 0m))
            {
                output.Add(Slice(firm.Document, source, -firm.Netted, false));
                firm.Netted = 0m;
            }
        }

        foreach (var ret in returns)
        {
            ret.Netted = sign > 0 ? ret.Capacity : 0m;
            output.Add(Slice(ret.Document, source, -sign * ret.Capacity, false));
        }
    }

    private static void Fill(FinancialSettlement payment, decimal amount, List<Slot> slots, List<EffectiveSettlement> output)
    {
        var remaining = amount;
        foreach (var slot in slots)
        {
            if (remaining <= 0m) break;
            var piece = Math.Min(slot.Free, remaining);
            if (piece <= 0m) continue;
            slot.Cash += piece;
            remaining -= piece;
            output.Add(Slice(slot.Document, payment, piece, true));
        }
    }

    private static void Unwind(FinancialSettlement reversal, List<Slot> slots, List<EffectiveSettlement> output)
    {
        var remaining = -reversal.Amount;
        for (var i = slots.Count - 1; i >= 0 && remaining > 0m; i--)
        {
            var piece = Math.Min(slots[i].Cash, remaining);
            if (piece <= 0m) continue;
            slots[i].Cash -= piece;
            remaining -= piece;
            output.Add(Slice(slots[i].Document, reversal, -piece, true));
        }
    }

    // Juros/multa/desconto rateados pela proporção do pedaço na baixa real.
    // Abatimento da devolução não é dinheiro: sem juros/multa/desconto.
    private static EffectiveSettlement Slice(FinancialDocument document, FinancialSettlement source, decimal piece, bool prorate)
    {
        var ratio = !prorate || source.Amount == 0m ? 0m : piece / source.Amount;
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
