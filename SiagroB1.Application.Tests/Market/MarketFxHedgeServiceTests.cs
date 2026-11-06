using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services.Market;
using SiagroB1.Application.Services.Market.Hedges;
using SiagroB1.Application.Services.Market.Tickets;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.Market.MarketFuturesTestHelpers;

namespace SiagroB1.Application.Tests.Market;

public class MarketFxMathTests
{
    [Fact]
    public void Sell_SettledBelowContracted_IsGain() =>
        Assert.Equal(10000m, MarketFxMath.RealizedPnlBrl(MarketTradeSide.Sell, 5.50m, 5.40m, 100000m));

    [Fact]
    public void Buy_SettledBelowContracted_IsLoss() =>
        Assert.Equal(-10000m, MarketFxMath.RealizedPnlBrl(MarketTradeSide.Buy, 5.50m, 5.40m, 100000m));

    [Fact]
    public void Rounds_TwoPlaces_AwayFromZero()
    {
        Assert.Equal(0.01m, MarketFxMath.RealizedPnlBrl(MarketTradeSide.Sell, 5.000500m, 5m, 10m));
        Assert.Equal(-0.01m, MarketFxMath.RealizedPnlBrl(MarketTradeSide.Buy, 5.000500m, 5m, 10m));
    }
}

public class MarketFxHedgeServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static MarketFxHedgeService Service(UnitOfWork db, params string[] granted) =>
        new(db.Context, new MarketPermissionGuard(new FakeUserPermissions(granted.Length == 0 ? [Manage] : granted)),
            Clock(), new KeyedAsyncLock(), new FakeCompanyContext("0001"));

    private static MarketFxHedgeInput In(
        MarketTradeSide side = MarketTradeSide.Sell, int accountId = 2, decimal amount = 100000m, decimal rate = 5.50m,
        DateOnly? settlement = null, Guid? purchaseKey = null, Guid? salesKey = null) =>
        new(new DateTime(2026, 11, 5, 14, 0, 0, DateTimeKind.Utc), accountId, MarketFxHedgeKind.Ndf, side, amount, rate,
            settlement ?? new DateOnly(2026, 12, 15), "NDF-1", purchaseKey, salesKey, null);

    [Fact]
    public async Task Create_SetsDerivedFields()
    {
        var db = await SeedAsync();
        var h = await Service(db).CreateAsync(In(purchaseKey: PurchaseKey), "joao", Ct);
        Assert.True(h.Id > 0);
        Assert.Equal(MarketFxHedgeStatus.Open, h.Status);
        Assert.Equal(new DateTime(2026, 11, 5), h.TradeDate);
        Assert.Equal(new DateTime(2026, 12, 15), h.SettlementDate);
        Assert.Equal("CC000010", h.ContractCode);
        Assert.Equal("joao", h.CreatedBy);
        Assert.False(h.Confirmed);
    }

    [Fact]
    public async Task Create_NoPermission_Forbidden()
    {
        var db = await SeedAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(db, "X").CreateAsync(In(), "u", Ct));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public async Task Create_Invalid_BusinessException(int scenario)
    {
        var db = await SeedAsync();
        db.Context.MarketCounterparties.Add(new MarketCounterparty
        {
            Id = 9, Name = "Banco Off", Type = MarketCounterpartyType.Bank, Active = true,
            Accounts = [new MarketCounterpartyAccount { Id = 9, CounterpartyId = 9, Code = "B-OFF", Active = false }],
        });
        await db.SaveChangesAsync();
        var input = scenario switch
        {
            0 => In(amount: 0m),
            1 => In(rate: 0m),
            2 => In(accountId: 1), // conta de corretora
            3 => In(accountId: 9), // conta inativa
            4 => In(accountId: 99),
            5 => In(settlement: new DateOnly(2026, 11, 4)),
            6 => In(purchaseKey: PurchaseKey, salesKey: SalesKey),
            _ => In(salesKey: Guid.NewGuid()),
        };
        await Assert.ThrowsAsync<BusinessException>(() => Service(db).CreateAsync(input, "u", Ct));
    }

    [Fact]
    public async Task Create_SettlementOnTradeDate_Allowed()
    {
        var db = await SeedAsync();
        var h = await Service(db).CreateAsync(In(settlement: new DateOnly(2026, 11, 5)), "u", Ct);
        Assert.Equal(new DateTime(2026, 11, 5), h.SettlementDate);
    }

    [Fact]
    public async Task Settle_Sell_StoresPositiveResult()
    {
        var db = await SeedAsync();
        var svc = Service(db);
        var h = await svc.CreateAsync(In(), "u", Ct);
        var s = await svc.SettleAsync(h.Id, 5.40m, "maria", Ct);
        Assert.Equal(MarketFxHedgeStatus.Settled, s.Status);
        Assert.Equal(5.40m, s.SettlementRate);
        Assert.Equal(10000m, s.RealizedPnlBrl);
        Assert.Equal("maria", s.SettledBy);
        Assert.Equal(new DateTime(2026, 11, 6, 15, 0, 0, DateTimeKind.Utc), s.SettledAt);
    }

    [Fact]
    public async Task Settle_Buy_StoresNegativeResult()
    {
        var db = await SeedAsync();
        var svc = Service(db);
        var h = await svc.CreateAsync(In(MarketTradeSide.Buy), "u", Ct);
        Assert.Equal(-10000m, (await svc.SettleAsync(h.Id, 5.40m, "u", Ct)).RealizedPnlBrl);
    }

    [Fact]
    public async Task Settle_Twice_Canceled_ZeroRate_Throw()
    {
        var db = await SeedAsync();
        var svc = Service(db);
        var h = await svc.CreateAsync(In(), "u", Ct);
        await Assert.ThrowsAsync<BusinessException>(() => svc.SettleAsync(h.Id, 0m, "u", Ct));
        await svc.SettleAsync(h.Id, 5.40m, "u", Ct);
        await Assert.ThrowsAsync<BusinessException>(() => svc.SettleAsync(h.Id, 5.30m, "u", Ct));

        var c = await svc.CreateAsync(In(), "u", Ct);
        await svc.CancelAsync(c.Id, "u", Ct);
        await Assert.ThrowsAsync<BusinessException>(() => svc.SettleAsync(c.Id, 5.30m, "u", Ct));
    }

    [Fact]
    public async Task Settle_ConfirmedHedge_IsAllowed()
    {
        var db = await SeedAsync();
        var svc = Service(db);
        var h = await svc.CreateAsync(In(), "u", Ct);
        await svc.SetConfirmedAsync(h.Id, true, "u", Ct);
        Assert.Equal(MarketFxHedgeStatus.Settled, (await svc.SettleAsync(h.Id, 5.40m, "u", Ct)).Status);
    }

    [Fact]
    public async Task Update_Settled_Throws_Open_Works()
    {
        var db = await SeedAsync();
        var svc = Service(db);
        var h = await svc.CreateAsync(In(), "u", Ct);
        var up = await svc.UpdateAsync(h.Id, In(amount: 50000m), "ana", Ct);
        Assert.Equal(50000m, up.AmountUsd);
        Assert.Equal("ana", up.UpdatedBy);
        await svc.SettleAsync(h.Id, 5.40m, "u", Ct);
        await Assert.ThrowsAsync<BusinessException>(() => svc.UpdateAsync(h.Id, In(), "u", Ct));
        await Assert.ThrowsAsync<BusinessException>(() => svc.DeleteAsync(h.Id, "u", Ct));
    }

    [Fact]
    public async Task Update_Delete_Confirmed_Throw_ThenUnconfirmAllows()
    {
        var db = await SeedAsync();
        var svc = Service(db);
        var h = await svc.CreateAsync(In(), "u", Ct);
        var c = await svc.SetConfirmedAsync(h.Id, true, "bia", Ct);
        Assert.True(c.Confirmed);
        Assert.Equal("bia", c.ConfirmedBy);
        Assert.NotNull(c.ConfirmedAt);
        await Assert.ThrowsAsync<BusinessException>(() => svc.UpdateAsync(h.Id, In(), "u", Ct));
        await Assert.ThrowsAsync<BusinessException>(() => svc.DeleteAsync(h.Id, "u", Ct));
        var u = await svc.SetConfirmedAsync(h.Id, false, "u", Ct);
        Assert.False(u.Confirmed);
        Assert.Null(u.ConfirmedBy);
        await svc.DeleteAsync(h.Id, "u", Ct);
        Assert.Empty(await db.Context.MarketFxHedges.ToListAsync());
    }

    [Fact]
    public async Task Reopen_FromSettled_ClearsSettlement()
    {
        var db = await SeedAsync();
        var svc = Service(db);
        var h = await svc.CreateAsync(In(), "u", Ct);
        await svc.SettleAsync(h.Id, 5.40m, "u", Ct);
        var r = await svc.ReopenAsync(h.Id, "u", Ct);
        Assert.Equal(MarketFxHedgeStatus.Open, r.Status);
        Assert.Null(r.SettlementRate);
        Assert.Null(r.SettledAt);
        Assert.Null(r.SettledBy);
        Assert.Null(r.RealizedPnlBrl);
    }

    [Fact]
    public async Task Cancel_Open_ThenReopen_AndCancelSettledThrows()
    {
        var db = await SeedAsync();
        var svc = Service(db);
        var h = await svc.CreateAsync(In(), "u", Ct);
        Assert.Equal(MarketFxHedgeStatus.Canceled, (await svc.CancelAsync(h.Id, "u", Ct)).Status);
        await Assert.ThrowsAsync<BusinessException>(() => svc.CancelAsync(h.Id, "u", Ct));
        Assert.Equal(MarketFxHedgeStatus.Open, (await svc.ReopenAsync(h.Id, "u", Ct)).Status);
        await Assert.ThrowsAsync<BusinessException>(() => svc.ReopenAsync(h.Id, "u", Ct));
        await svc.SettleAsync(h.Id, 5.4m, "u", Ct);
        await Assert.ThrowsAsync<BusinessException>(() => svc.CancelAsync(h.Id, "u", Ct));
    }

    [Fact]
    public async Task Unknown_Id_KeyNotFound_And_Forbidden_OnWrites()
    {
        var db = await SeedAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Service(db).SettleAsync(99, 5m, "u", Ct));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Service(db).SuggestSettlementRateAsync(99, Ct));
        var h = await Service(db).CreateAsync(In(), "u", Ct);
        var denied = Service(db, "X");
        await Assert.ThrowsAsync<ForbiddenException>(() => denied.SettleAsync(h.Id, 5m, "u", Ct));
        await Assert.ThrowsAsync<ForbiddenException>(() => denied.ReopenAsync(h.Id, "u", Ct));
        await Assert.ThrowsAsync<ForbiddenException>(() => denied.CancelAsync(h.Id, "u", Ct));
        await Assert.ThrowsAsync<ForbiddenException>(() => denied.SetConfirmedAsync(h.Id, true, "u", Ct));
        await Assert.ThrowsAsync<ForbiddenException>(() => denied.UpdateAsync(h.Id, In(), "u", Ct));
        await Assert.ThrowsAsync<ForbiddenException>(() => denied.DeleteAsync(h.Id, "u", Ct));
    }

    private static async Task<MarketFxHedge> SeedForSuggestionAsync(UnitOfWork db, string? spot)
    {
        db.Context.MarketTradingSettings.Add(new MarketTradingSettings { SpotFxInstrumentCode = spot });
        await db.SaveChangesAsync();
        return await Service(db).CreateAsync(In(settlement: new DateOnly(2026, 12, 15)), "u", Ct);
    }

    [Fact]
    public async Task Suggest_NoSpotInstrument_Nulls()
    {
        var db = await SeedAsync();
        var h = await SeedForSuggestionAsync(db, null);
        var s = await Service(db).SuggestSettlementRateAsync(h.Id, Ct);
        Assert.Null(s.Rate);
        Assert.Null(s.Source);
        Assert.Null(s.Date);
    }

    [Fact]
    public async Task Suggest_UsesDailyCloseOfSettlementDate()
    {
        var db = await SeedAsync();
        var h = await SeedForSuggestionAsync(db, "SOYSPOT");
        db.Context.MarketDailyCloses.AddRange(
            new MarketDailyClose { InstrumentCode = "SOYSPOT", Date = new DateTime(2026, 12, 15), Close = 5.43m },
            new MarketDailyClose { InstrumentCode = "SOYSPOT", Date = new DateTime(2026, 12, 14), Close = 5.10m });
        db.Context.MarketQuotes.Add(new MarketQuote
        {
            InstrumentCode = "SOYSPOT", Last = 5.99m, QuotedAt = new DateTime(2026, 11, 6, 15, 0, 0, DateTimeKind.Utc),
        });
        await db.SaveChangesAsync();
        var s = await Service(db).SuggestSettlementRateAsync(h.Id, Ct);
        Assert.Equal(5.43m, s.Rate);
        Assert.Equal("dailyClose", s.Source);
        Assert.Equal(new DateOnly(2026, 12, 15), s.Date);
    }

    [Fact]
    public async Task Suggest_FallsBackToLastQuote_WithSaoPauloDate()
    {
        var db = await SeedAsync();
        var h = await SeedForSuggestionAsync(db, "SOYSPOT");
        db.Context.MarketQuotes.Add(new MarketQuote
        {
            InstrumentCode = "SOYSPOT", Last = 5.61m, QuotedAt = new DateTime(2026, 11, 6, 1, 0, 0, DateTimeKind.Utc),
        });
        await db.SaveChangesAsync();
        var s = await Service(db).SuggestSettlementRateAsync(h.Id, Ct);
        Assert.Equal(5.61m, s.Rate);
        Assert.Equal("lastQuote", s.Source);
        Assert.Equal(new DateOnly(2026, 11, 5), s.Date);
    }

    [Fact]
    public async Task Suggest_NothingAvailable_Nulls()
    {
        var db = await SeedAsync();
        var h = await SeedForSuggestionAsync(db, "SOYSPOT");
        var s = await Service(db).SuggestSettlementRateAsync(h.Id, Ct);
        Assert.Null(s.Rate);
        Assert.Null(s.Source);
    }
}

public class MarketFxHedgesControllerTests
{
    private static SiagroB1.Web.Controllers.MarketFxHedgesController Controller(UnitOfWork db, params string[] granted) =>
        new(db.Context,
            new MarketFxHedgeService(db.Context,
                new MarketPermissionGuard(new FakeUserPermissions(granted.Length == 0 ? [Manage] : granted)), Clock(),
                new KeyedAsyncLock(), new FakeCompanyContext("0001")),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SiagroB1.Web.Controllers.MarketFxHedgesController>.Instance)
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

    private static MarketFxHedgeInput In(int accountId = 2) =>
        new(new DateTime(2026, 11, 5, 14, 0, 0, DateTimeKind.Utc), accountId, MarketFxHedgeKind.Forward,
            MarketTradeSide.Sell, 100000m, 5.50m, new DateOnly(2026, 12, 15), null, null, null, null);

    private static string Json(Microsoft.AspNetCore.Mvc.JsonResult r) =>
        System.Text.Json.JsonSerializer.Serialize(r.Value, SiagroB1.Web.Market.MarketQuotesStreamLoop.JsonOptions);

    [Fact]
    public async Task Create_Settle_ReturnCamelCase_AndErrorsArePlainText()
    {
        var db = await SeedAsync();
        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.JsonResult>(await Controller(db).Create(In(), CancellationToken.None));
        var json = Json(ok);
        Assert.Contains("\"side\":\"sell\"", json);
        Assert.Contains("\"kind\":\"forward\"", json);
        Assert.Contains("\"status\":\"open\"", json);
        Assert.Contains("\"settlementDate\":\"2026-12-15", json);
        Assert.DoesNotContain("\"account\"", json);

        var settled = Assert.IsType<Microsoft.AspNetCore.Mvc.JsonResult>(await Controller(db).Settle(
            1, new SiagroB1.Web.Controllers.MarketFxHedgeSettleInput(5.40m), CancellationToken.None));
        Assert.Contains("\"realizedPnlBrl\":10000", Json(settled));

        var bad = Assert.IsType<Microsoft.AspNetCore.Mvc.ContentResult>(await Controller(db).Create(In(1), CancellationToken.None));
        Assert.Equal(400, bad.StatusCode);
        Assert.Equal("text/plain; charset=utf-8", bad.ContentType);
        var forbidden = Assert.IsType<Microsoft.AspNetCore.Mvc.ContentResult>(await Controller(db, "X").Cancel(1, CancellationToken.None));
        Assert.Equal(403, forbidden.StatusCode);
        var missing = Assert.IsType<Microsoft.AspNetCore.Mvc.ContentResult>(await Controller(db).Remove(99, CancellationToken.None));
        Assert.Equal(404, missing.StatusCode);
        var noBody = Assert.IsType<Microsoft.AspNetCore.Mvc.ContentResult>(await Controller(db).Settle(1, null, CancellationToken.None));
        Assert.Equal(400, noBody.StatusCode);
    }

    [Fact]
    public async Task SuggestedRate_ReturnsRateSourceDate()
    {
        var db = await SeedAsync();
        await Controller(db).Create(In(), CancellationToken.None);
        var r = Assert.IsType<Microsoft.AspNetCore.Mvc.JsonResult>(await Controller(db).SuggestedRate(1, CancellationToken.None));
        Assert.Equal("{\"rate\":null,\"source\":null,\"date\":null}", Json(r));
        var missing = Assert.IsType<Microsoft.AspNetCore.Mvc.ContentResult>(await Controller(db).SuggestedRate(99, CancellationToken.None));
        Assert.Equal(404, missing.StatusCode);
    }
}
