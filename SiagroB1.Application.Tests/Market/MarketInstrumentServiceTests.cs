using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Market;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using static SiagroB1.Application.Tests.Market.MarketTestHelpers;

namespace SiagroB1.Application.Tests.Market;

public class MarketInstrumentServiceTests
{
    [Fact]
    public async Task CreateSamples_IsIdempotent()
    {
        var db = TestDb.CreateUnitOfWork();
        var service = new MarketInstrumentService(db.Context, new TestLogger<MarketInstrumentService>());

        var first = await service.CreateSamplesAsync(CancellationToken.None);
        var second = await service.CreateSamplesAsync(CancellationToken.None);

        Assert.Equal(16, first);
        Assert.Equal(0, second);
        Assert.Equal(16, await db.Context.MarketInstruments.CountAsync());
        var soy = await db.Context.MarketInstruments.SingleAsync(i => i.Code == "ZSF7");
        Assert.Equal(0.36745m, soy.TonFactor);
        Assert.Equal(MarketExchange.Cbot, soy.Exchange);
        Assert.Equal(30, soy.CmaSourceId);
        Assert.Equal(57, (await db.Context.MarketInstruments.SingleAsync(i => i.Code == "CCMF27")).CmaSourceId);
        Assert.Equal(200, (await db.Context.MarketInstruments.SingleAsync(i => i.Code == "DOL COM")).CmaSourceId);
        var curve = await db.Context.MarketInstruments.SingleAsync(i => i.Code == "CURVA DE DOLAR 90D");
        Assert.Equal(90, curve.CurveDays);
        Assert.Equal(87, curve.CmaSourceId);
        Assert.Equal(MarketInstrumentType.CurvePoint, curve.Type);
    }
    [Fact]
    public async Task CreateSamples_SortOrder_InBlocksPerGroup()
    {
        var db = TestDb.CreateUnitOfWork();
        await Service(db).CreateSamplesAsync(CancellationToken.None);

        var orders = await db.Context.MarketInstruments.AsNoTracking().ToDictionaryAsync(i => i.Code, i => i.SortOrder);

        Assert.Equal([10, 11, 12, 13, 14], new[] { "ZSF7", "ZSH7", "ZSK7", "ZSN7", "ZSX6" }.Select(c => orders[c]));
        Assert.Equal([20, 21, 22], new[] { "ZCH7", "ZCK7", "ZCN7" }.Select(c => orders[c]));
        Assert.Equal([30, 31], new[] { "CCMF27", "CCMH27" }.Select(c => orders[c]));
        Assert.Equal(40, orders["DOL COM"]);
        Assert.Equal([50, 51, 52, 53, 54], new[] { 30, 60, 90, 180, 360 }.Select(d => orders[$"CURVA DE DOLAR {d}D"]));
    }

    [Fact]
    public async Task CreateSamples_DoesNotTouchExistingRows()
    {
        var db = TestDb.CreateUnitOfWork();
        var existing = Instrument("ZSF7");
        existing.SortOrder = 99;
        db.Context.MarketInstruments.Add(existing);
        await db.SaveChangesAsync();

        Assert.Equal(15, await Service(db).CreateSamplesAsync(CancellationToken.None));
        Assert.Equal(99, (await db.Context.MarketInstruments.AsNoTracking().SingleAsync(i => i.Code == "ZSF7")).SortOrder);
    }

    [Fact]
    public async Task Delete_WithQuote_Throws()
    {
        var db = await SeedAsync(Instrument("ZSF7"), Instrument("ZSH7"), Instrument("FREE"));
        db.Context.MarketQuotes.Add(new MarketQuote { InstrumentCode = "ZSF7", Last = 1m });
        db.Context.MarketDailyCloses.Add(new MarketDailyClose { InstrumentCode = "ZSH7", Close = 1m });
        await db.SaveChangesAsync();
        var service = Service(db);

        var e = await Assert.ThrowsAsync<BusinessException>(() => service.DeleteAsync("ZSF7"));
        Assert.Equal("Instrumento possui cotações ou fechamentos; inative-o em vez de excluir.", e.Message);
        await Assert.ThrowsAsync<BusinessException>(() => service.DeleteAsync("ZSH7"));
        Assert.True(await service.DeleteAsync("FREE"));
    }

    public static TheoryData<string, decimal, decimal> InvalidInstruments => new()
    {
        { "", 1m, 1m },
        { "   ", 1m, 1m },
        { "A/B", 1m, 1m },
        { "A\\B", 1m, 1m },
        { "A'B", 1m, 1m },
        { "A%B", 1m, 1m },
        { "A?B", 1m, 1m },
        { "A#B", 1m, 1m },
        { "OK", 0m, 1m },
        { "OK", 1m, -1m },
    };

    [Theory]
    [MemberData(nameof(InvalidInstruments))]
    public async Task Create_Invalid_Throws(string code, decimal tonFactor, decimal contractSize)
    {
        var db = TestDb.CreateUnitOfWork();
        var instrument = Instrument(code);
        instrument.TonFactor = tonFactor;
        instrument.ContractSize = contractSize;

        await Assert.ThrowsAsync<BusinessException>(() => Service(db).CreateAsync(instrument));
        Assert.Equal(0, await db.Context.MarketInstruments.CountAsync());
    }

    [Fact]
    public async Task Create_TrimsCode()
    {
        var db = TestDb.CreateUnitOfWork();

        await Service(db).CreateAsync(Instrument("  ZSF7 "));

        Assert.Equal("ZSF7", (await db.Context.MarketInstruments.AsNoTracking().SingleAsync()).Code);
    }

    [Fact]
    public async Task Update_Invalid_Throws()
    {
        var db = await SeedAsync(Instrument("ZSF7"));
        var tracked = await db.Context.MarketInstruments.SingleAsync();
        tracked.TonFactor = 0m;

        await Assert.ThrowsAsync<BusinessException>(() => Service(db).UpdateAsync("ZSF7", tracked));
    }

    private static MarketInstrumentService Service(SiagroB1.Infra.UnitOfWork db) =>
        new(db.Context, new TestLogger<MarketInstrumentService>());
}
