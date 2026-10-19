using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
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

    /// <summary>
    /// Extrato da conta no período: saldo anterior, linhas em ordem de data com saldo acumulado, totais e saldo final.
    /// Com <paramref name="onlyReconciled"/> considera só linhas conciliadas, inclusive no saldo anterior, para o
    /// acumulado fechar com o extrato do banco.
    /// </summary>
    public static async Task<FinancialAccountMovementsDto> StatementAsync(AppDbContext ctx, string accountCode,
        DateTime dateFrom, DateTime dateTo, bool onlyReconciled, CancellationToken ct)
    {
        var from = dateFrom.Date;
        var until = dateTo.Date.AddDays(1);

        var query = Query(ctx, accountCode);
        if (onlyReconciled) query = query.Where(r => r.ReconciliationKey != null);

        var opening = await query.Where(r => r.Date < from).SumAsync(r => r.Amount, ct);
        var rows = await query.Where(r => r.Date >= from && r.Date < until)
            .OrderBy(r => r.Date).ThenBy(r => r.CreatedAt)
            .ToListAsync(ct);

        var balance = opening;
        var dtos = new List<FinancialAccountMovementDto>(rows.Count);
        foreach (var row in rows)
        {
            balance += row.Amount;
            var dto = ToDto(row);
            dto.RunningBalance = balance;
            dtos.Add(dto);
        }

        return new FinancialAccountMovementsDto
        {
            OpeningBalance = opening,
            ClosingBalance = balance,
            TotalIn = rows.Where(r => r.Amount > 0).Sum(r => r.Amount),
            TotalOut = -rows.Where(r => r.Amount < 0).Sum(r => r.Amount),
            Rows = dtos
        };
    }

    public static FinancialAccountMovementDto ToDto(FinancialAccountMovementRow x, bool marked = false) => new()
    {
        Source = x.Source,
        Key = x.Key,
        Date = x.Date,
        Amount = x.Amount,
        Description = x.Description,
        DocumentReference = x.DocumentReference,
        CardCode = x.CardCode,
        CardName = x.CardName,
        FinancialDocumentKey = x.FinancialDocumentKey,
        SettlementOrigin = x.SettlementOrigin,
        ReconciliationKey = x.ReconciliationKey,
        Reconciled = x.ReconciliationKey != null,
        Marked = marked
    };
}
