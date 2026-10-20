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
