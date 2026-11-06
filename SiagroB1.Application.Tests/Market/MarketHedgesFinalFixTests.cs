using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using static SiagroB1.Application.Tests.Market.MarketFuturesTestHelpers;

namespace SiagroB1.Application.Tests.Market;

/// <summary>Menores da revisão final da parte 3: datas REST, instrumento com operações, edição, data futura, cotação.</summary>
public class MarketHedgesFinalFixTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static string Text(IActionResult result)
    {
        var json = Assert.IsType<JsonResult>(result);
        return JsonSerializer.Serialize(json.Value, (JsonSerializerOptions)json.SerializerSettings!);
    }

    // ---------- H5 ----------

    [Fact]
    public async Task EfpRest_BusinessDatesAsYyyyMmDd()
    {
        var name = await MarketEfpServiceTests.SeedEfpAsync();
        var created = Text(await MarketEfpsControllerTests.Controller(TestDb.CreateUnitOfWork(name))
            .Create(MarketEfpServiceTests.SoyInput(), Ct));
        Assert.Contains("\"date\":\"2026-11-05\"", created);
        Assert.Contains("\"financialDueDate\":\"2026-12-15\"", created);

        var contracts = Text(await MarketEfpsControllerTests.Controller(TestDb.CreateUnitOfWork(name))
            .Contracts("purchase", null, Ct));
        Assert.Contains("\"standardCashFlowDate\":\"2026-12-15\"", contracts);
    }

    [Fact]
    public void JsonDates_ConvertsDateTimeAndKeepsNull()
    {
        var node = JsonSerializer.SerializeToNode(new { tradeDate = new DateTime(2026, 11, 6), settlementDate = (DateTime?)null })!
            .AsObject();
        SiagroB1.Web.Market.MarketJsonDates.AsDateOnly(node, "tradeDate", "settlementDate", "missing");
        Assert.Equal("{\"tradeDate\":\"2026-11-06\",\"settlementDate\":null}", node.ToJsonString());
    }

    // ---------- H6 ----------

    [Fact]
    public async Task Instrument_WithTrade_CannotChangeSizeUnitOrFactor_NorBeDeleted()
    {
        var db = await SeedAsync();
        await TradeService(db).CreateAsync(Input(MarketTradeSide.Buy, 1, 1180m), "u", Ct);
        var svc = MarketTestHelpers.InstrumentService(TestDbCopy(db));

        var changed = Instrument(Soy, MarketQuoteUnit.UsCentsPerBushel, 1000m, 0.36745m);
        var e = await Assert.ThrowsAsync<BusinessException>(() => svc.UpdateAsync(Soy, changed));
        Assert.Contains("operações de bolsa", e.Message);

        var renamed = Instrument(Soy, MarketQuoteUnit.UsCentsPerBushel, 5000m, 0.36745m);
        renamed.Description = "Soja nova";
        Assert.NotNull(await MarketTestHelpers.InstrumentService(TestDbCopy(db)).UpdateAsync(Soy, renamed));

        var d = await Assert.ThrowsAsync<BusinessException>(() =>
            MarketTestHelpers.InstrumentService(TestDbCopy(db)).DeleteAsync(Soy));
        Assert.Contains("operações de bolsa ou EFP", d.Message);
    }

    private static SiagroB1.Infra.UnitOfWork TestDbCopy(SiagroB1.Infra.UnitOfWork db)
    {
        db.Context.ChangeTracker.Clear();
        return db;
    }

    // ---------- H9 ----------

    [Fact]
    public async Task Update_InactiveAccountAndInstrumentUnchanged_Allowed_ChangingToInactiveRefused()
    {
        var db = await SeedAsync();
        var trade = await TradeService(db).CreateAsync(Input(MarketTradeSide.Buy, 1, 1180m), "u", Ct);
        db.Context.ChangeTracker.Clear();
        (await db.Context.MarketCounterpartyAccounts.SingleAsync(a => a.Id == 1)).Active = false;
        (await db.Context.MarketInstruments.SingleAsync(i => i.Code == Soy)).Active = false;
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var updated = await TradeService(db).UpdateAsync(trade.Id, Input(MarketTradeSide.Buy, 2, 1181m), "u", Ct);
        Assert.Equal(2, updated.Lots);

        db.Context.ChangeTracker.Clear();
        var e = await Assert.ThrowsAsync<BusinessException>(() =>
            TradeService(db).UpdateAsync(trade.Id, Input(MarketTradeSide.Buy, 2, 1181m, "ZSOFF"), "u", Ct));
        Assert.Contains("inativo", e.Message);
        db.Context.ChangeTracker.Clear();
        var a = await Assert.ThrowsAsync<BusinessException>(() =>
            TradeService(db).UpdateAsync(trade.Id, Input(MarketTradeSide.Buy, 2, 1181m, accountId: 4), "u", Ct));
        Assert.Contains("inativa", a.Message);
    }

    // ---------- H10 ----------

    [Fact]
    public async Task TradedAt_UntilEndOfTodayInSaoPaulo()
    {
        var db = await SeedAsync();
        // Agora: 06/11 12:00 em SP. 23:30 de SP (02:30 UTC do dia 7) ainda é hoje; 00:30 do dia 7 em SP é futuro.
        await TradeService(db).CreateAsync(Input(MarketTradeSide.Buy, 1, 1180m,
            tradedAt: new DateTime(2026, 11, 7, 2, 30, 0, DateTimeKind.Utc)), "u", Ct);
        var e = await Assert.ThrowsAsync<BusinessException>(() => TradeService(db).CreateAsync(Input(MarketTradeSide.Buy, 1,
            1180m, tradedAt: new DateTime(2026, 11, 7, 3, 30, 0, DateTimeKind.Utc)), "u", Ct));
        Assert.Equal("A data do negócio não pode estar no futuro.", e.Message);
    }

    // ---------- H11 ----------

    [Fact]
    public async Task OpenPositions_CarryQuoteTimestampAndSource()
    {
        var db = await SeedAsync();
        await TradeService(db).CreateAsync(Input(MarketTradeSide.Buy, 1, 1180m), "u", Ct);
        db.Context.MarketQuotes.Add(new MarketQuote
        {
            InstrumentCode = Soy, Last = 1220m, QuotedAt = new DateTime(2026, 11, 6, 14, 0, 0),
            Source = MarketQuoteSource.Manual,
        });
        await db.SaveChangesAsync();

        var soy = (await PositionService(db).GetOpenPositionsAsync(Ct)).Single();

        Assert.Equal(new DateTime(2026, 11, 6, 14, 0, 0, DateTimeKind.Utc), soy.LastQuotedAt);
        Assert.Equal(DateTimeKind.Utc, soy.LastQuotedAt!.Value.Kind);
        Assert.Equal(MarketQuoteSource.Manual, soy.LastQuoteSource);
    }
}
