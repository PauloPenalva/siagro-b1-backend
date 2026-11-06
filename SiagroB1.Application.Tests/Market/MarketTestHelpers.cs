using SiagroB1.Application.Services.Market;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Market;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Market;

internal static class MarketTestHelpers
{
    public static MarketInstrument Instrument(string code, bool active = true) => new()
    {
        Code = code,
        Description = code,
        Commodity = "SOY",
        GroupName = "Soja CBOT",
        TonFactor = 1m,
        ContractSize = 1m,
        Active = active,
    };

    public static async Task<UnitOfWork> SeedAsync(params MarketInstrument[] instruments)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.MarketInstruments.AddRange(instruments);
        await db.SaveChangesAsync();
        return db;
    }

    public static MarketQuoteService QuoteService(UnitOfWork db, LiveQuoteStore? store = null, string company = "0001") =>
        new(db.Context, store ?? new LiveQuoteStore(), new FakeCompanyContext(company), new TestLogger<MarketQuoteService>());

    public static MarketInstrumentService InstrumentService(UnitOfWork db, LiveQuoteStore? store = null, string company = "0001") =>
        new(db.Context, store ?? new LiveQuoteStore(), new FakeCompanyContext(company), new TestLogger<MarketInstrumentService>());

    public static MarketQuoteUpdate Update(string symbol, decimal? last, bool settlement = false) =>
        new(symbol, last, null, null, null, null, null, null, null, settlement, null);
}
