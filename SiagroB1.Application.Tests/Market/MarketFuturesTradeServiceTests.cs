using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Market.Hedges;
using SiagroB1.Application.Services.Market.Tickets;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.Market.MarketFuturesTestHelpers;

namespace SiagroB1.Application.Tests.Market;

public class MarketFuturesTradeServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static DateTime At(int day, int hour) => new(2026, 11, day, hour, 0, 0, DateTimeKind.Utc);

    private static async Task<(int buy, int sell)> BuyTenSellFourAsync(UnitOfWork db)
    {
        var svc = TradeService(db);
        var buy = await svc.CreateAsync(Input(MarketTradeSide.Buy, 10, 1180m, tradedAt: At(4, 14)), "u", Ct);
        var sell = await svc.CreateAsync(Input(MarketTradeSide.Sell, 4, 1192.5m, tradedAt: At(5, 14)), "u", Ct);
        db.Context.ChangeTracker.Clear();
        return (buy.Id, sell.Id);
    }

    [Fact]
    public async Task Create_BuyThenSell_OneMatchWithRealizedPnl()
    {
        var db = await SeedAsync();
        var (buy, sell) = await BuyTenSellFourAsync(db);

        var match = Assert.Single(await db.Context.MarketFuturesMatches.ToListAsync());
        Assert.Equal(buy, match.OpenTradeId);
        Assert.Equal(sell, match.CloseTradeId);
        Assert.Equal(4, match.Lots);
        Assert.Equal(2500m, match.RealizedPnl); // 12,5 × 4 × 50
        Assert.Equal(CurrencyType.Usd, match.Currency);
        Assert.Equal(new DateTime(2026, 11, 5), match.CloseDate);
        var trades = await db.Context.MarketFuturesTrades.OrderBy(t => t.Id).ToListAsync();
        Assert.Equal([6, 0], trades.Select(t => t.OpenLots));
    }

    [Fact]
    public async Task Create_ExampleSoy_SixThousandTwoHundredFifty()
    {
        var db = await SeedAsync();
        var svc = TradeService(db);
        await svc.CreateAsync(Input(MarketTradeSide.Buy, 10, 1180m, tradedAt: At(4, 14)), "u", Ct);
        await svc.CreateAsync(Input(MarketTradeSide.Sell, 10, 1192.5m, tradedAt: At(5, 14)), "u", Ct);
        Assert.Equal(6250m, (await db.Context.MarketFuturesMatches.SingleAsync()).RealizedPnl);
    }

    [Fact]
    public async Task Create_SetsTradeDateFromSaoPauloAndContractCode()
    {
        var db = await SeedAsync();
        var trade = await TradeService(db).CreateAsync(
            Input(MarketTradeSide.Buy, 1, 1180m, tradedAt: At(6, 1), purchaseKey: PurchaseKey), "joao", Ct);
        Assert.Equal(new DateTime(2026, 11, 5), trade.TradeDate);
        Assert.Equal("CC000010", trade.ContractCode);
        Assert.Equal("joao", trade.CreatedBy);
        Assert.Equal(1, trade.OpenLots);
    }

    [Fact]
    public async Task Create_SalesContract_CopiesCode()
    {
        var db = await SeedAsync();
        var trade = await TradeService(db).CreateAsync(Input(MarketTradeSide.Sell, 1, 1180m, salesKey: SalesKey), "u", Ct);
        Assert.Equal("CV000020", trade.ContractCode);
    }

    [Fact]
    public async Task Update_Price_RecomputesPnl()
    {
        var db = await SeedAsync();
        var (buy, _) = await BuyTenSellFourAsync(db);
        await TradeService(db).UpdateAsync(buy, Input(MarketTradeSide.Buy, 10, 1190m, tradedAt: At(4, 14)), "u", Ct);
        db.Context.ChangeTracker.Clear();
        Assert.Equal(500m, (await db.Context.MarketFuturesMatches.SingleAsync()).RealizedPnl);
    }

    [Fact]
    public async Task Delete_ClosingTrade_RemovesMatchAndReopens()
    {
        var db = await SeedAsync();
        var (buy, sell) = await BuyTenSellFourAsync(db);
        await TradeService(db).DeleteAsync(sell, "u", Ct);
        db.Context.ChangeTracker.Clear();
        Assert.Empty(await db.Context.MarketFuturesMatches.ToListAsync());
        Assert.Equal(10, (await db.Context.MarketFuturesTrades.SingleAsync(t => t.Id == buy)).OpenLots);
    }

    [Fact]
    public async Task Update_MoveToOtherInstrument_RematchesBothPairs()
    {
        var db = await SeedAsync();
        var (buy, sell) = await BuyTenSellFourAsync(db);
        await TradeService(db).UpdateAsync(sell, Input(MarketTradeSide.Sell, 4, 1192.5m, SoyMar, tradedAt: At(5, 14)), "u", Ct);
        db.Context.ChangeTracker.Clear();
        Assert.Empty(await db.Context.MarketFuturesMatches.ToListAsync());
        Assert.Equal(10, (await db.Context.MarketFuturesTrades.SingleAsync(t => t.Id == buy)).OpenLots);
        Assert.Equal(4, (await db.Context.MarketFuturesTrades.SingleAsync(t => t.Id == sell)).OpenLots);
    }

    [Theory]
    [InlineData(2, Soy, 1)]          // conta de banco
    [InlineData(4, Soy, 1)]          // conta inativa
    [InlineData(1, "SOYSPOT", 1)]    // spot
    [InlineData(1, "ZSOFF", 1)]      // inativo
    [InlineData(1, "ZSZERO", 1)]     // ContractSize 0
    [InlineData(1, "NOPE", 1)]       // inexistente
    [InlineData(1, Soy, 0)]          // lotes 0
    public async Task Create_Invalid_BusinessException(int account, string instrument, int lots)
    {
        var db = await SeedAsync();
        await Assert.ThrowsAsync<BusinessException>(() =>
            TradeService(db).CreateAsync(Input(MarketTradeSide.Buy, lots, 1180m, instrument, account), "u", Ct));
        Assert.Empty(await db.Context.MarketFuturesTrades.ToListAsync());
    }

    [Fact]
    public async Task Create_InvalidValues_BusinessException()
    {
        var db = await SeedAsync();
        var svc = TradeService(db);
        await Assert.ThrowsAsync<BusinessException>(() => svc.CreateAsync(Input(MarketTradeSide.Buy, 1, 0m), "u", Ct));
        await Assert.ThrowsAsync<BusinessException>(() =>
            svc.CreateAsync(Input(MarketTradeSide.Buy, 1, 1m) with { Fees = -1m }, "u", Ct));
        await Assert.ThrowsAsync<BusinessException>(() =>
            svc.CreateAsync(Input(MarketTradeSide.Buy, 1, 1m, purchaseKey: PurchaseKey, salesKey: SalesKey), "u", Ct));
        await Assert.ThrowsAsync<BusinessException>(() =>
            svc.CreateAsync(Input(MarketTradeSide.Buy, 1, 1m, purchaseKey: Guid.NewGuid()), "u", Ct));
        await Assert.ThrowsAsync<BusinessException>(() =>
            svc.CreateAsync(Input(MarketTradeSide.Buy, 1, 1m, tradedAt: At(8, 15)), "u", Ct)); // futuro
        await Assert.ThrowsAsync<BusinessException>(() =>
            svc.CreateAsync(Input(MarketTradeSide.Buy, 1, 1m, reason: MarketFuturesTradeReason.Efp), "u", Ct));
    }

    [Fact]
    public async Task Writes_WithoutPermission_Forbidden()
    {
        var db = await SeedAsync();
        var (buy, _) = await BuyTenSellFourAsync(db);
        var svc = TradeService(db, "OTHER");
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.CreateAsync(Input(MarketTradeSide.Buy, 1, 1m), "u", Ct));
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.UpdateAsync(buy, Input(MarketTradeSide.Buy, 1, 1m), "u", Ct));
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.DeleteAsync(buy, "u", Ct));
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.SetConfirmedAsync(buy, true, "u", Ct));
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.ConfirmDayAsync(1, new DateOnly(2026, 11, 4), "u", Ct));
    }

    [Fact]
    public async Task EfpTrade_ProtectedFromRest_ButEfpPathWorks()
    {
        var db = await SeedAsync();
        var svc = TradeService(db);
        var trade = await svc.CreateForEfpAsync(
            Input(MarketTradeSide.Sell, 2, 1180m, reason: MarketFuturesTradeReason.Efp), 7, "u", Ct);
        Assert.Equal(7, trade.EfpId);
        await Assert.ThrowsAsync<BusinessException>(() => svc.UpdateAsync(trade.Id, Input(MarketTradeSide.Sell, 2, 1m), "u", Ct));
        await Assert.ThrowsAsync<BusinessException>(() => svc.DeleteAsync(trade.Id, "u", Ct));
        await Assert.ThrowsAsync<BusinessException>(() => svc.DeleteForEfpAsync(trade.Id, 8, "u", Ct));
        await svc.DeleteForEfpAsync(trade.Id, 7, "u", Ct);
        Assert.Empty(await db.Context.MarketFuturesTrades.ToListAsync());
    }

    [Fact]
    public async Task ConfirmedTrade_Protected_UntilUnconfirmed()
    {
        var db = await SeedAsync();
        var (buy, _) = await BuyTenSellFourAsync(db);
        var svc = TradeService(db);
        var confirmed = await svc.SetConfirmedAsync(buy, true, "maria", Ct);
        Assert.True(confirmed.Confirmed);
        Assert.Equal("maria", confirmed.ConfirmedBy);
        await Assert.ThrowsAsync<BusinessException>(() => svc.DeleteAsync(buy, "u", Ct));
        await Assert.ThrowsAsync<BusinessException>(() =>
            svc.UpdateAsync(buy, Input(MarketTradeSide.Buy, 10, 1190m, tradedAt: At(4, 14)), "u", Ct));
        var open = await svc.SetConfirmedAsync(buy, false, "maria", Ct);
        Assert.False(open.Confirmed);
        Assert.Null(open.ConfirmedBy);
        await svc.UpdateAsync(buy, Input(MarketTradeSide.Buy, 10, 1190m, tradedAt: At(4, 14)), "u", Ct);
    }

    [Fact]
    public async Task ConfirmDay_OnlyThatDayAndAccount()
    {
        var db = await SeedAsync();
        var svc = TradeService(db);
        await svc.CreateAsync(Input(MarketTradeSide.Buy, 1, 1m, tradedAt: At(4, 14)), "u", Ct);
        await svc.CreateAsync(Input(MarketTradeSide.Buy, 1, 1m, Corn, tradedAt: At(4, 15)), "u", Ct);
        await svc.CreateAsync(Input(MarketTradeSide.Buy, 1, 1m, tradedAt: At(5, 14)), "u", Ct);
        await svc.CreateAsync(Input(MarketTradeSide.Buy, 1, 1m, accountId: 3, tradedAt: At(4, 14)), "u", Ct);

        Assert.Equal(2, await svc.ConfirmDayAsync(1, new DateOnly(2026, 11, 4), "u", Ct));
        db.Context.ChangeTracker.Clear();
        var confirmed = await db.Context.MarketFuturesTrades.Where(t => t.Confirmed).ToListAsync();
        Assert.Equal(2, confirmed.Count);
        Assert.All(confirmed, t => Assert.Equal(1, t.AccountId));
        Assert.All(confirmed, t => Assert.Equal(new DateTime(2026, 11, 4), t.TradeDate));
    }

    [Fact]
    public async Task Update_Missing_KeyNotFound()
    {
        var db = await SeedAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => TradeService(db).DeleteAsync(99, "u", Ct));
    }

    [Fact]
    public async Task Rematch_UsesCurrentContractSize()
    {
        var db = await SeedAsync();
        await BuyTenSellFourAsync(db);
        var instrument = await db.Context.MarketInstruments.SingleAsync(i => i.Code == Soy);
        instrument.ContractSize = 10000m;
        await db.SaveChangesAsync();
        await TradeService(db).RematchAsync(1, Soy, Ct);
        db.Context.ChangeTracker.Clear();
        Assert.Equal(5000m, (await db.Context.MarketFuturesMatches.SingleAsync()).RealizedPnl);
    }

    [Fact]
    public async Task LotInfo_SoyTonsPerLot()
    {
        var db = await SeedAsync();
        var info = await TradeService(db).GetLotInfoAsync(Soy, Ct);
        Assert.Equal(136.0729m, Math.Round(info.TonsPerLot!.Value, 4));
        Assert.Null(info.UsdPerLot);
        Assert.Equal(CurrencyType.Usd, info.Currency);
        var dollar = await TradeService(db).GetLotInfoAsync(Dollar, Ct);
        Assert.Equal(50000m, dollar.UsdPerLot);
        Assert.Null(dollar.TonsPerLot);
    }

    [Fact]
    public async Task Concurrent_Creates_SameResultAsSequential()
    {
        var name = Guid.NewGuid().ToString();
        (await SeedAsync(name)).Context.Dispose();
        var locks = new KeyedAsyncLock();
        var a = TradeService(TestDb.CreateUnitOfWork(name), locks);
        var b = TradeService(TestDb.CreateUnitOfWork(name), locks);

        await Task.WhenAll(
            a.CreateAsync(Input(MarketTradeSide.Buy, 10, 1180m, tradedAt: At(4, 14)), "u", Ct),
            b.CreateAsync(Input(MarketTradeSide.Sell, 4, 1192.5m, tradedAt: At(5, 14)), "u", Ct));

        var check = TestDb.CreateUnitOfWork(name);
        var match = Assert.Single(await check.Context.MarketFuturesMatches.ToListAsync());
        Assert.Equal(4, match.Lots);
        Assert.Equal(2500m, match.RealizedPnl);
        var trades = await check.Context.MarketFuturesTrades.ToListAsync();
        Assert.Equal(6, trades.Single(t => t.Side == MarketTradeSide.Buy).OpenLots);
        Assert.Equal(0, trades.Single(t => t.Side == MarketTradeSide.Sell).OpenLots);
    }
}

/// <summary>REST das operações: erros em text/plain pt-BR e a resposta com <c>tons</c> com sinal.</summary>
public class MarketFuturesTradesControllerTests
{
    private static SiagroB1.Web.Controllers.MarketFuturesTradesController Controller(UnitOfWork db, params string[] granted) =>
        new(db.Context, TradeService(db, granted), PositionService(db),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SiagroB1.Web.Controllers.MarketFuturesTradesController>.Instance)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
                {
                    User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                        [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "u")], "test")),
                },
            },
        };

    [Fact]
    public async Task Create_ReturnsSignedTons_AndErrorsArePlainText()
    {
        var db = await SeedAsync();
        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.JsonResult>(await Controller(db).Create(
            Input(MarketTradeSide.Sell, 2, 1180m), CancellationToken.None));
        var node = Assert.IsType<System.Text.Json.Nodes.JsonObject>(ok.Value);
        Assert.Equal(-272.1459m, node["tons"]!.GetValue<decimal>());
        Assert.Equal(2, node["openLots"]!.GetValue<int>());
        Assert.Equal("sell", node["side"]!.GetValue<string>());

        var bad = Assert.IsType<Microsoft.AspNetCore.Mvc.ContentResult>(await Controller(db).Create(
            Input(MarketTradeSide.Buy, 1, 1m, accountId: 2), CancellationToken.None));
        Assert.Equal(400, bad.StatusCode);
        Assert.Equal("text/plain; charset=utf-8", bad.ContentType);

        var forbidden = Assert.IsType<Microsoft.AspNetCore.Mvc.ContentResult>(await Controller(db, "X").Remove(1, CancellationToken.None));
        Assert.Equal(403, forbidden.StatusCode);
        var missing = Assert.IsType<Microsoft.AspNetCore.Mvc.ContentResult>(await Controller(db).Remove(99, CancellationToken.None));
        Assert.Equal(404, missing.StatusCode);
    }
}
