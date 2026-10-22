using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;
using SiagroB1.Infra.FiscalStock;

namespace SiagroB1.Application.Services.Accounting.Postings;

/// <summary>
/// CMV do mês pelo custo PEPS congelado no fechamento do estoque fiscal (670) e estorno na reabertura (675). O custo de
/// uma saída só para de mudar quando o mês fecha: antes disso uma entrada retroativa recalcula tudo (spec §8).
/// </summary>
public static class CostOfSalesPosting
{
    public const string DocumentType = "FiscalStockPeriod";
    public const string CloseEvent = "Close";
    public const string ReopenEvent = "Reopen";

    /// <summary>Custo das saídas do mês menos o custo devolvido pelas devoluções de venda do mês, por produto.</summary>
    public static Dictionary<string, decimal> CostOfSalesByItem(IEnumerable<(string ItemCode, FifoResult Result)> ledgers,
        DateTime first, DateTime last)
    {
        var result = new Dictionary<string, decimal>();
        foreach (var (itemCode, ledger) in ledgers)
        {
            var month = ledger.Lines.Where(l => l.Movement.Date >= first && l.Movement.Date <= last).ToList();
            var cost = month.Where(l => l.Movement.Kind == InventoryMovementKind.Exit).Sum(l => l.ValueOut)
                       - month.Where(l => l.Movement.Kind == InventoryMovementKind.SalesReturn).Sum(l => l.ValueIn);
            if (cost != 0) result[itemCode] = cost;
        }

        return result;
    }

    public static async Task CloseAsync(IAccountingPostingService? engine, AppDbContext db, FiscalStockPeriod period,
        IReadOnlyDictionary<string, decimal> costByItem, string user)
    {
        if (engine is null) return;
        await engine.PostAsync("670", await ContextAsync(db, period, costByItem, CloseEvent), user);
    }

    public static async Task ReopenAsync(IAccountingPostingService? engine, AppDbContext db, FiscalStockPeriod period, string user)
    {
        if (engine is null) return;
        await engine.PostReversalAsync("675", await ContextAsync(db, period, new Dictionary<string, decimal>(), ReopenEvent),
            CloseEvent, user);
    }

    private static async Task<PostingContext> ContextAsync(AppDbContext db, FiscalStockPeriod period,
        IReadOnlyDictionary<string, decimal> costByItem, string evt)
    {
        var codes = costByItem.Keys.ToList();
        var accounts = await db.Items.AsNoTracking().Where(i => codes.Contains(i.ItemCode))
            .ToDictionaryAsync(i => i.ItemCode, i => i.LedgerAccountCode);
        var rows = costByItem.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new PostingRow(
            new Dictionary<string, string?> { ["Item.LedgerAccount"] = accounts.GetValueOrDefault(x.Key) },
            new Dictionary<string, decimal> { ["Item.CostOfSales"] = x.Value },
            null, null, null,
            new Dictionary<string, string> { ["{ItemCode}"] = x.Key })).ToList();
        var last = new DateTime(period.Year, period.Month, 1).AddMonths(1).AddDays(-1);
        var date = evt == CloseEvent ? last : DateTime.Today;
        return new PostingContext(DocumentType, period.Key.ToString(), evt, date, period.BranchCode, null, rows);
    }
}
