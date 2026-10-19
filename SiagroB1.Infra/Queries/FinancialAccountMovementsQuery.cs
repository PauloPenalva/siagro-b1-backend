using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Infra.Queries;

/// <summary>
/// Movimento da conta financeira = baixas do ledger com conta + lançamentos manuais. Fonte única para a tela de
/// lançamentos, a reconciliação e o extrato (Web e Reports usam o mesmo AppDbContext).
///
/// Sinal de caixa da baixa: (Amount + juros + multa − desconto), positivo a receber, negativo a pagar. O estorno já é
/// linha negativa, então sai invertido sozinho. Baixa sem conta (abatimento, compensação...) não é caixa e fica fora.
/// </summary>
public static class FinancialAccountMovementsQuery
{
    public static IQueryable<FinancialAccountMovementRow> Query(AppDbContext ctx, string accountCode)
    {
        var settlements = ctx.FinancialSettlements
            .Where(s => s.FinancialAccountCode == accountCode)
            .Select(s => new FinancialAccountMovementRow
            {
                Source = FinancialAccountMovementSource.Settlement,
                Key = s.Key,
                Date = s.SettlementDate,
                CreatedAt = s.CreatedAt,
                Amount = (s.Amount + s.InterestAmount + s.FineAmount - s.DiscountAmount) *
                         (s.FinancialDocument!.Direction == FinancialDirection.Receivable ? 1m : -1m),
                Description = (s.Origin == FinancialSettlementOrigin.Reversal ? "Estorno de baixa " : "Baixa ") +
                              s.FinancialDocument.Code + " - " + (s.FinancialDocument.CardName ?? s.FinancialDocument.CardCode),
                DocumentReference = s.DocumentReference,
                CardCode = s.FinancialDocument.CardCode,
                CardName = s.FinancialDocument.CardName,
                FinancialDocumentKey = s.FinancialDocumentKey,
                SettlementOrigin = s.Origin,
                ReconciliationKey = s.ReconciliationKey
            });

        var entries = ctx.FinancialAccountEntries
            .Where(e => e.FinancialAccountCode == accountCode)
            .Select(e => new FinancialAccountMovementRow
            {
                Source = FinancialAccountMovementSource.Manual,
                Key = e.Key,
                Date = e.EntryDate,
                CreatedAt = e.CreatedAt,
                Amount = e.Amount,
                Description = e.Description,
                DocumentReference = e.DocumentReference,
                CardCode = null,
                CardName = null,
                FinancialDocumentKey = null,
                SettlementOrigin = null,
                ReconciliationKey = e.ReconciliationKey
            });

        return settlements.Concat(entries);
    }

    /// <summary>Saldo da conta antes do DIA de <paramref name="date"/> (exclusivo).</summary>
    public static async Task<decimal> BalanceBeforeAsync(AppDbContext ctx, string accountCode, DateTime date,
        CancellationToken ct)
    {
        var day = date.Date;
        return await Query(ctx, accountCode).Where(r => r.Date < day).SumAsync(r => r.Amount, ct);
    }
}
