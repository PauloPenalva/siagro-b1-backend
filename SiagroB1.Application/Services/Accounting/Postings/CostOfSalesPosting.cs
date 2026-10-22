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

    /// <summary>
    /// Por produto, o custo PEPS das saídas de VENDA do mês (CFOP x.1xx: 5.1xx, 6.1xx, 7.1xx) e, à parte, o custo
    /// devolvido pelas devoluções de venda do mês. Remessa, transferência e outras saídas com CFOP fora de venda movem o
    /// estoque mas não são custo da venda. Os dois valores ficam separados: um mês só de devoluções não some num líquido
    /// negativo.
    /// </summary>
    public static Dictionary<string, (decimal Cost, decimal ReturnCost)> CostOfSalesByItem(
        IEnumerable<(string ItemCode, FifoResult Result)> ledgers, DateTime first, DateTime last)
    {
        var result = new Dictionary<string, (decimal, decimal)>();
        foreach (var (itemCode, ledger) in ledgers)
        {
            var month = ledger.Lines.Where(l => l.Movement.Date >= first && l.Movement.Date <= last).ToList();
            var cost = month.Where(l => l.Movement.Kind == InventoryMovementKind.Exit && IsSaleCfop(l.Movement)).Sum(l => l.ValueOut);
            var returnCost = month.Where(l => l.Movement.Kind == InventoryMovementKind.SalesReturn).Sum(l => l.ValueIn);
            if (cost != 0 || returnCost != 0) result[itemCode] = (cost, returnCost);
        }

        return result;
    }

    private static bool IsSaleCfop(InventoryMovement movement)
    {
        var cfop = (movement.Source as FiscalStockLineSource)?.Cfop?.Replace(".", string.Empty).Trim();
        return cfop is { Length: 4 } && cfop[0] is '5' or '6' or '7' && cfop[1] == '1';
    }

    public static async Task CloseAsync(IAccountingPostingService? engine, AppDbContext db, FiscalStockPeriod period,
        IReadOnlyDictionary<string, (decimal Cost, decimal ReturnCost)> costByItem, string user)
    {
        if (engine is null) return;
        await engine.PostAsync("670", await ContextAsync(db, period, costByItem, CloseEvent), user);
    }

    public static async Task ReopenAsync(IAccountingPostingService? engine, AppDbContext db, FiscalStockPeriod period, string user)
    {
        if (engine is null) return;
        await engine.PostReversalAsync("675", await ContextAsync(db, period, new Dictionary<string, (decimal, decimal)>(), ReopenEvent),
            CloseEvent, user);
    }

    private static async Task<PostingContext> ContextAsync(AppDbContext db, FiscalStockPeriod period,
        IReadOnlyDictionary<string, (decimal Cost, decimal ReturnCost)> costByItem, string evt)
    {
        var codes = costByItem.Keys.ToList();
        var accounts = await db.Items.AsNoTracking().Where(i => codes.Contains(i.ItemCode))
            .ToDictionaryAsync(i => i.ItemCode, i => i.LedgerAccountCode);
        var rows = costByItem.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new PostingRow(
            new Dictionary<string, string?> { ["Item.LedgerAccount"] = accounts.GetValueOrDefault(x.Key) },
            new Dictionary<string, decimal> { ["Item.CostOfSales"] = x.Value.Cost, ["Item.SalesReturnCost"] = x.Value.ReturnCost },
            null, null, null,
            new Dictionary<string, string> { ["{ItemCode}"] = x.Key })).ToList();
        var last = new DateTime(period.Year, period.Month, 1).AddMonths(1).AddDays(-1);
        var date = evt == CloseEvent ? last : DateTime.Today;
        // O fechamento do estoque fiscal não é recusado por mês contábil fechado: o CMV cai no 1º dia aberto.
        return new PostingContext(DocumentType, period.Key.ToString(), evt, date, period.BranchCode, null, rows,
            ExternalEvent: evt == CloseEvent);
    }
}
