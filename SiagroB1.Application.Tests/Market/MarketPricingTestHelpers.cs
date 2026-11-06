using SiagroB1.Application.Services.Market;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Market;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Market;

internal static class MarketPricingTestHelpers
{
    public static MarketFreightRateService FreightService(UnitOfWork db, LiveQuoteStore? store = null) =>
        new(db.Context, store ?? new LiveQuoteStore(), new FakeCompanyContext("0001"), new TestLogger<MarketFreightRateService>());

    public static MarketDestinationService DestinationService(UnitOfWork db, LiveQuoteStore? store = null) =>
        new(db.Context, store ?? new LiveQuoteStore(), new FakeCompanyContext("0001"), new TestLogger<MarketDestinationService>());

    public static MarketPriceTableService TableService(UnitOfWork db, LiveQuoteStore? store = null) =>
        new(db.Context, new FakeItemService(new() { ["SOJA"] = "SOJA EM GRAOS" }), store ?? new LiveQuoteStore(),
            new FakeCompanyContext("0001"), new TestLogger<MarketPriceTableService>());

    public static MarketPriceTableLineService LineService(UnitOfWork db, LiveQuoteStore? store = null) =>
        new(db.Context, store ?? new LiveQuoteStore(), new FakeCompanyContext("0001"), new TestLogger<MarketPriceTableLineService>());

    public static MarketTradingSettingsService SettingsService(UnitOfWork db, params string[] granted) =>
        new(db.Context, new MarketPermissionGuard(new FakeUserPermissions(granted)), new LiveQuoteStore(),
            new FakeCompanyContext("0001"));

    public static MarketInstrument Usd(string code = "ZSF7") =>
        Configure(Instrument(code), MarketInstrumentType.Future, MarketQuoteUnit.UsCentsPerBushel);

    public static MarketInstrument Brl(string code = "CCMF27") =>
        Configure(Instrument(code), MarketInstrumentType.Future, MarketQuoteUnit.BrlPerBag);

    public static MarketInstrument Curve(string code = "CURVA 30D", int days = 30) =>
        Configure(Instrument(code), MarketInstrumentType.CurvePoint, MarketQuoteUnit.BrlPerUsd, days);

    public static MarketInstrument SpotFx(string code = "DOL COM") =>
        Configure(Instrument(code), MarketInstrumentType.Spot, MarketQuoteUnit.BrlPerUsd);

    private static MarketInstrument Configure(MarketInstrument i, MarketInstrumentType type, MarketQuoteUnit unit, int? days = null)
    {
        i.Type = type;
        i.QuoteUnit = unit;
        i.CurveDays = days;
        return i;
    }

    private static MarketInstrument Instrument(string code) => MarketTestHelpers.Instrument(code);

    /// <summary>Semeia destino, safra, tabela e instrumentos; devolve o banco.</summary>
    public static async Task<UnitOfWork> SeedPricingAsync(params MarketInstrument[] instruments)
    {
        var db = await MarketTestHelpers.SeedAsync(instruments);
        db.Context.MarketDestinations.Add(new MarketDestination { Id = 1, Name = "Paranaguá" });
        db.Context.HarvestSeasons.Add(new HarvestSeason { Code = "26/27", Name = "Safra 26/27" });
        db.Context.Municipalities.Add(new Municipality { Code = "4104808", Name = "Cascavel", StateAbbreviation = "PR" });
        db.Context.MarketPriceTables.Add(new MarketPriceTable
        {
            Id = 1, Description = "Soja", ItemCode = "SOJA", ItemName = "SOJA", HarvestSeasonCode = "26/27",
        });
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
        return db;
    }

    public static MarketPriceTableLine Line(string instrument, string? curve = null) => new()
    {
        PriceTableId = 1,
        Description = "Jan/27",
        InstrumentCode = instrument,
        FxCurveInstrumentCode = curve,
        DestinationId = 1,
        PaymentDate = new DateTime(2027, 4, 15),
        ReceiptDate = new DateTime(2027, 3, 15),
    };
}
