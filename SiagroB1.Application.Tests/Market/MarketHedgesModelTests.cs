using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using static SiagroB1.Application.Tests.Market.MarketTestHelpers;

namespace SiagroB1.Application.Tests.Market;

public class MarketHedgesModelTests
{
    [Fact]
    public async Task HedgeEntities_PersistAndReadBack()
    {
        var db = await SeedAsync(Instrument("ZSF27"));
        var ctx = db.Context;

        var broker = new MarketCounterparty
        {
            Name = "Corretora Alfa",
            Type = MarketCounterpartyType.Broker,
            Accounts = [new() { Code = "A-1" }, new() { Code = "A-2", Description = "Conta secundária" }],
        };
        var bank = new MarketCounterparty { Name = "Banco Beta", Type = MarketCounterpartyType.Bank };
        ctx.MarketCounterparties.AddRange(broker, bank);
        await ctx.SaveChangesAsync();
        var account = broker.Accounts[0];
        var bankAccount = new MarketCounterpartyAccount { CounterpartyId = bank.Id, Code = "B-1" };
        ctx.MarketCounterpartyAccounts.Add(bankAccount);

        var tradedAt = new DateTime(2026, 11, 6, 14, 30, 0, DateTimeKind.Utc);
        var open = new MarketFuturesTrade
        {
            TradedAt = tradedAt, TradeDate = new DateTime(2026, 11, 6), AccountId = account.Id, InstrumentCode = "ZSF27",
            Side = MarketTradeSide.Buy, Lots = 10, OpenLots = 0, Price = 1180m, Fees = 12.5m, FeesCurrency = CurrencyType.Usd,
            Reason = MarketFuturesTradeReason.Normal, PurchaseContractKey = Guid.NewGuid(), CreatedAt = tradedAt,
        };
        var close = new MarketFuturesTrade
        {
            TradedAt = tradedAt.AddDays(1), TradeDate = new DateTime(2026, 11, 7), AccountId = account.Id, InstrumentCode = "ZSF27",
            Side = MarketTradeSide.Sell, Lots = 10, OpenLots = 0, Price = 1192.5m, FeesCurrency = CurrencyType.Usd,
            Reason = MarketFuturesTradeReason.Normal, CreatedAt = tradedAt,
        };
        ctx.MarketFuturesTrades.AddRange(open, close);
        await ctx.SaveChangesAsync();

        ctx.MarketFuturesMatches.Add(new MarketFuturesMatch
        {
            AccountId = account.Id, InstrumentCode = "ZSF27", OpenTradeId = open.Id, CloseTradeId = close.Id, Lots = 10,
            OpenPrice = 1180m, ClosePrice = 1192.5m, RealizedPnl = 6250m, Currency = CurrencyType.Usd, CloseDate = new DateTime(2026, 11, 7),
        });
        ctx.MarketFxHedges.Add(new MarketFxHedge
        {
            TradedAt = tradedAt, TradeDate = new DateTime(2026, 11, 6), AccountId = bankAccount.Id, Kind = MarketFxHedgeKind.Ndf,
            Side = MarketTradeSide.Sell, AmountUsd = 100000m, ContractedRate = 5.45m, SettlementDate = new DateTime(2027, 3, 1),
            Status = MarketFxHedgeStatus.Open, CreatedAt = tradedAt,
        });
        ctx.MarketEfps.Add(new MarketEfp
        {
            Date = new DateTime(2026, 11, 6), AccountId = account.Id, InstrumentCode = "ZSF27", Side = MarketTradeSide.Sell, Lots = 2,
            Price = 1180m, Premium = 50m, ContractSide = MarketEfpContractSide.Purchase, ContractKey = Guid.NewGuid(), ContractCode = "C-1",
            VolumeKg = 272000m, FixationPrice = 2.12345678m, FinancialDueDate = new DateTime(2026, 12, 1),
            Status = MarketEfpStatus.Processing, CreatedAt = tradedAt,
        });
        ctx.MarketBrokerStatements.Add(new MarketBrokerStatement
        {
            AccountId = account.Id, Date = new DateTime(2026, 11, 6), DailyAdjustment = -350.25m, MarginRequired = 9000m,
            MarginDeposited = 10000m, Currency = CurrencyType.Usd, CreatedAt = tradedAt,
        });
        await ctx.SaveChangesAsync();

        ctx.ChangeTracker.Clear();
        var counterparty = await ctx.MarketCounterparties.Include(c => c.Accounts).SingleAsync(c => c.Name == "Corretora Alfa");
        Assert.Equal(2, counterparty.Accounts.Count);
        var match = await ctx.MarketFuturesMatches.Include(m => m.OpenTrade).Include(m => m.CloseTrade).SingleAsync();
        Assert.Equal(MarketTradeSide.Buy, match.OpenTrade!.Side);
        Assert.Equal(MarketTradeSide.Sell, match.CloseTrade!.Side);
        Assert.Equal(6250m, match.RealizedPnl);
        Assert.Equal(open.PurchaseContractKey, (await ctx.MarketFuturesTrades.FindAsync(open.Id))!.PurchaseContractKey);
        Assert.Equal(100000m, (await ctx.MarketFxHedges.SingleAsync()).AmountUsd);
        Assert.Equal(2.12345678m, (await ctx.MarketEfps.SingleAsync()).FixationPrice);
        Assert.Equal(-350.25m, (await ctx.MarketBrokerStatements.SingleAsync()).DailyAdjustment);
    }

    [Fact]
    public async Task Model_CascadesOnlyCounterpartyAccounts_AndMatchFksAreRestrict()
    {
        var db = await SeedAsync();
        var model = db.Context.Model;
        var fks = model.FindEntityType(typeof(MarketFuturesMatch))!.GetForeignKeys().ToList();
        Assert.Equal(2, fks.Count);
        Assert.All(fks, fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
        Assert.Equal(DeleteBehavior.Cascade,
            model.FindEntityType(typeof(MarketCounterpartyAccount))!.GetForeignKeys().Single().DeleteBehavior);
        foreach (var t in new[] { typeof(MarketFuturesTrade), typeof(MarketFxHedge), typeof(MarketEfp), typeof(MarketBrokerStatement) })
            Assert.All(model.FindEntityType(t)!.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
    }
}
