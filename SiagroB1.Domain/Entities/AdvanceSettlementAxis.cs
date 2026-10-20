using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Eixos do ledger do ADIANTAMENTO (spec 2026-10-14, compensação de adiantamento): aplicado = compensações e
/// transferências de SAÍDA (e os estornos delas); pago = todo o resto, inclusive a transferência de ENTRADA.
/// Fonte única para o recálculo do saldo (FinancialDocumentsRecalculateBalanceService) e para a posição na data-base
/// dos relatórios (FinancialPositionCalculator).
/// </summary>
public static class AdvanceSettlementAxis
{
    /// <summary>
    /// Transferência de ENTRADA: a linha do adiantamento NOVO, nascido do desmembramento, que aponta para o original
    /// (OriginType = FinancialDocument e OriginKey = RelatedDocumentKey). Depende só de dados do próprio documento.
    /// </summary>
    public static bool IsIncomingTransfer(FinancialDocument advance, FinancialSettlement line) =>
        line.Origin == FinancialSettlementOrigin.AdvanceTransfer &&
        advance.OriginType == FinancialDocumentOrigin.FinancialDocument &&
        advance.OriginKey is not null &&
        line.RelatedDocumentKey == advance.OriginKey;

    private static bool IsAppliedAxis(FinancialDocument advance, FinancialSettlement line) =>
        line.Origin == FinancialSettlementOrigin.AdvanceApplication ||
        (line.Origin == FinancialSettlementOrigin.AdvanceTransfer && !IsIncomingTransfer(advance, line));

    /// <summary>O estorno segue o eixo da linha que ele estorna — a espelho fica sempre no mesmo título.</summary>
    public static bool CountsAsApplied(
        FinancialDocument advance, FinancialSettlement line, IReadOnlyDictionary<Guid, FinancialSettlement> byKey) =>
        line.Origin == FinancialSettlementOrigin.Reversal
            ? line.ReversedSettlementKey is { } reversed &&
              byKey.TryGetValue(reversed, out var original) &&
              IsAppliedAxis(advance, original)
            : IsAppliedAxis(advance, line);
}
