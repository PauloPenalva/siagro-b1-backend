using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market.Hedges;

/// <summary>Uso de contas do Mercado: operação de bolsa, hedge de moeda, EFP ou extrato de corretora.</summary>
internal static class MarketAccountUsage
{
    public static async Task<bool> IsUsedAsync(AppDbContext context, IQueryable<int> accountIds) =>
        await context.MarketFuturesTrades.AnyAsync(t => accountIds.Contains(t.AccountId))
        || await context.MarketFxHedges.AnyAsync(h => accountIds.Contains(h.AccountId))
        || await context.MarketEfps.AnyAsync(e => accountIds.Contains(e.AccountId))
        || await context.MarketBrokerStatements.AnyAsync(s => accountIds.Contains(s.AccountId));
}
