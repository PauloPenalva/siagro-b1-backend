using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Infra.Queries;

/// <summary>
/// Saldo a baixar no ITS por romaneio. Mora no Infra porque o Web (OData) e o Reports (PDF)
/// precisam da MESMA regra de elegibilidade, e o Reports não referencia o Application.
/// Saldo calculado (peso líquido − linhas de baixas ativas), não persistido.
/// </summary>
public static class RoyaltyBalanceQuery
{
    public static IQueryable<RoyaltyBalanceRow> Build(AppDbContext ctx) =>
        from st in ctx.StorageTransactions.AsNoTracking()
        where (st.TransactionStatus == StorageTransactionsStatus.Confirmed ||
               st.TransactionStatus == StorageTransactionsStatus.Invoiced)
              // Romaneios anteriores ao recurso (e compras de papel, como a da transferência de
              // titularidade) não têm situação: ficam fora.
              && st.RoyaltySituation != null
              && ctx.ItemComplements.Any(c => c.ItemCode == st.ItemCode && c.ControlsRoyalties)
              && (
                  // Recebimento — menos o da Entrada em armazenagem própria, que conta pela Compra.
                  (st.TransactionType == StorageTransactionType.Receipt &&
                   !ctx.StorageEntryTransactions.Any(e => e.ReceiptStorageTransactionKey == st.Key))
                  ||
                  // Compra (carregamento em terceiro) — menos a da Conferência de saldo de armazém e
                  // a original de uma Troca de liberação (espelhada por estorno, substituída pela nova).
                  (st.TransactionType == StorageTransactionType.Purchase &&
                   st.TransactionOrigin != TransactionCode.WarehouseReconciliation &&
                   !ctx.ShippingReleaseChanges.Any(c => c.OriginalPurchaseStorageTransactionKey == st.Key)))
        let writtenOff = ctx.RoyaltyWriteOffLines
            .Where(l => l.StorageTransactionKey == st.Key &&
                        l.RoyaltyWriteOff!.Status == RoyaltyWriteOffStatus.Active)
            .Sum(l => (decimal?)l.Weight) ?? 0m
        select new RoyaltyBalanceRow
        {
            StorageTransactionKey = st.Key,
            Code = st.Code,
            TransactionType = st.TransactionType,
            TransactionDate = st.TransactionDate,
            BranchCode = st.BranchCode,
            CardCode = st.CardCode,
            CardName = st.CardName,
            RoyaltyOriginCode = st.RoyaltyOriginCode,
            RoyaltyOriginName = st.RoyaltyOriginName,
            ItemCode = st.ItemCode,
            ItemName = st.ItemName,
            StorageAddressCode = st.StorageAddressCode,
            Situation = st.RoyaltySituation!.Value,
            RoyaltyTestCode = st.RoyaltyTestCode,
            NetWeight = st.NetWeight,
            WrittenOffWeight = writtenOff,
            Balance = st.NetWeight - writtenOff,
        };

    public static IQueryable<RoyaltyBalanceRow> Apply(IQueryable<RoyaltyBalanceRow> query, RoyaltyBalanceFilter filter)
    {
        if (filter.PendingOnly)
            query = query.Where(x => x.Balance > 0);
        if (filter.FromDate is { } from)
            query = query.Where(x => x.TransactionDate >= from.Date);
        if (filter.ToDate is { } to)
        {
            var toExclusive = to.Date.AddDays(1);
            query = query.Where(x => x.TransactionDate < toExclusive);
        }
        if (!string.IsNullOrWhiteSpace(filter.CardCode))
            query = query.Where(x => x.CardCode == filter.CardCode);
        if (!string.IsNullOrWhiteSpace(filter.OriginCode))
            query = query.Where(x => x.RoyaltyOriginCode == filter.OriginCode);
        if (!string.IsNullOrWhiteSpace(filter.ItemCode))
            query = query.Where(x => x.ItemCode == filter.ItemCode);
        if (filter.Situation is { } situation)
            query = query.Where(x => x.Situation == situation);
        if (!string.IsNullOrWhiteSpace(filter.StorageAddressCode))
            query = query.Where(x => x.StorageAddressCode == filter.StorageAddressCode);
        return query;
    }

    public static Task<List<RoyaltyBalanceTotal>> TotalsAsync(IQueryable<RoyaltyBalanceRow> query) =>
        query.GroupBy(x => x.Situation)
            .Select(g => new RoyaltyBalanceTotal
            {
                Situation = g.Key,
                Count = g.Count(),
                NetWeight = g.Sum(x => x.NetWeight),
                WrittenOffWeight = g.Sum(x => x.WrittenOffWeight),
                Balance = g.Sum(x => x.Balance),
            })
            .OrderBy(x => x.Situation)
            .ToListAsync();
}
