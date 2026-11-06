using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Market.Pricing;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.Market.MarketPricingTestHelpers;

namespace SiagroB1.Application.Tests.Market;

public class MarketPriceGridServiceTests
{
    private static readonly DateOnly Today = new(2026, 11, 6);
    private const string City = "4104808";

    private static MarketPriceGridService Service(UnitOfWork db) => new(db.Context, FreightService(db));

    private static MarketQuote Quote(string code, decimal? last, decimal? bid = null) => new()
    {
        InstrumentCode = code, Last = last, Bid = bid, QuotedAt = new DateTime(2026, 11, 6, 12, 0, 0), Source = MarketQuoteSource.Cma,
    };

    /// <summary>Linha S1 do oráculo (soja CBOT, dólar pela curva de 30 dias, destino 1 a R$ 180/t).</summary>
    private static MarketPriceTableLine S1()
    {
        var line = Line("ZSF7", "CURVA 30D");
        line.Premium = 50; line.ElevationCost = 8; line.FxDiscount = 0.02m;
        line.DailyInterestPercent = 0.033m; line.MarginPercent = 1.5m; line.TechnicalLossPercent = 0.3m;
        line.RuralTaxPercent = 1.5m; line.FreightCoefficientPercent = 10; line.DiscountValue = 0.5m; line.DiscountIsPercent = true;
        return line;
    }

    /// <summary>Banco com soja CBOT + dólar spot/curva, parâmetros, cotações do oráculo, frete 180 e a linha S1.</summary>
    private static async Task<UnitOfWork> SeedS1Async(bool configureSpot = true, bool freight = true)
    {
        var zs = Usd(); zs.TonFactor = 0.36745m;
        var db = await SeedPricingAsync(zs, SpotFx(), Curve(), Brl());
        var c = db.Context;
        c.MarketTradingSettings.Add(new MarketTradingSettings { SpotFxInstrumentCode = configureSpot ? "DOL COM" : null });
        c.MarketQuotes.AddRange(Quote("ZSF7", 1188.25m), Quote("DOL COM", 5.4m), Quote("CURVA 30D", 5.465m), Quote("CCMF27", 68.50m));
        if (freight)
            c.MarketFreightRates.Add(new MarketFreightRate
            {
                MunicipalityCode = City, DestinationId = 1, RatePerTon = 180, ValidFrom = new DateTime(2026, 1, 1),
            });
        c.MarketPriceTableLines.Add(S1());
        await db.SaveChangesAsync();
        c.ChangeTracker.Clear();
        return db;
    }

    [Fact]
    public async Task Grid_CalculatesS1_FromDatabaseRows()
    {
        var db = await SeedS1Async();

        var grid = await Service(db).GetGridAsync("26/27", City, Today, CancellationToken.None);

        var row = Assert.Single(grid.Rows);
        Assert.Equal(MarketPriceStatus.Ok, row.Status);
        Assert.Equal(137.67m, row.Price);
        Assert.Equal(5.7267m, row.NetFx);
        Assert.Equal(153.588363m, Math.Round(row.PortPrice!.Value, 6));
        Assert.Equal(5.4m, row.SpotFx);
        Assert.Equal(180m, row.FreightPerTon);
        Assert.Equal(1188.25m, row.ExchangeQuote);
        Assert.Equal(DateTimeKind.Utc, row.QuotedAt!.Value.Kind);
        Assert.Equal(MarketQuoteSource.Cma, row.QuoteSource);
        Assert.NotEmpty(row.Steps);
        Assert.Equal("26/27", grid.HarvestSeasonCode);
        Assert.Equal(City, grid.MunicipalityCode);
    }

    [Fact]
    public async Task Grid_UsesBid_FallsBackToLast()
    {
        var db = await SeedS1Async();
        var quote = await db.Context.MarketQuotes.SingleAsync(q => q.InstrumentCode == "ZSF7");
        quote.Last = 1000m; quote.Bid = 1188.25m;
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var withBid = (await Service(db).GetGridAsync("26/27", City, Today, CancellationToken.None)).Rows.Single();
        Assert.Equal(1188.25m, withBid.ExchangeQuote);
        Assert.Equal(1000m, withBid.ExchangeLast);
        Assert.Equal(137.67m, withBid.Price);

        quote = await db.Context.MarketQuotes.SingleAsync(q => q.InstrumentCode == "ZSF7");
        quote.Last = 1188.25m; quote.Bid = null;
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var withoutBid = (await Service(db).GetGridAsync("26/27", City, Today, CancellationToken.None)).Rows.Single();
        Assert.Equal(1188.25m, withoutBid.ExchangeQuote);
        Assert.Equal(137.67m, withoutBid.Price);
    }

    [Fact]
    public async Task Grid_MissingQuote_RowWithoutPrice_OthersStillPriced()
    {
        var db = await SeedS1Async();
        // B1 (milho B3, R$/saca) a R$ 120/t no destino 2; a soja perde a cotação.
        db.Context.MarketDestinations.Add(new MarketDestination { Id = 2, Name = "Santos" });
        db.Context.MarketFreightRates.Add(new MarketFreightRate
        {
            MunicipalityCode = City, DestinationId = 2, RatePerTon = 120, ValidFrom = new DateTime(2026, 1, 1),
        });
        var b1 = Line("CCMF27");
        b1.DestinationId = 2; b1.Premium = 1.5m; b1.DailyInterestPercent = 0.03m; b1.MarginPercent = 2;
        b1.TechnicalLossPercent = 0.25m; b1.FreightCoefficientPercent = 5; b1.DiscountValue = 1;
        b1.ReceiptDate = new DateTime(2027, 7, 1); b1.PaymentDate = new DateTime(2027, 7, 31); b1.SortOrder = 5;
        db.Context.MarketPriceTableLines.Add(b1);
        db.Context.MarketQuotes.Remove(await db.Context.MarketQuotes.SingleAsync(q => q.InstrumentCode == "ZSF7"));
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var rows = (await Service(db).GetGridAsync("26/27", City, Today, CancellationToken.None)).Rows;

        Assert.Equal(2, rows.Count);
        var soy = rows.Single(r => r.InstrumentCode == "ZSF7");
        Assert.Equal(MarketPriceStatus.MissingQuote, soy.Status);
        Assert.Null(soy.Price);
        var corn = rows.Single(r => r.InstrumentCode == "CCMF27");
        Assert.Equal(MarketPriceStatus.Ok, corn.Status);
        Assert.Equal(60.52m, corn.Price);
    }

    [Fact]
    public async Task Grid_InactiveInstrument_InvalidSetup()
    {
        var db = await SeedS1Async();
        (await db.Context.MarketInstruments.SingleAsync(i => i.Code == "ZSF7")).Active = false;
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var row = (await Service(db).GetGridAsync("26/27", City, Today, CancellationToken.None)).Rows.Single();

        Assert.Equal(MarketPriceStatus.InvalidSetup, row.Status);
        Assert.Null(row.Price);
    }

    [Fact]
    public async Task Grid_UsdLineWithoutValidCurve_InvalidSetup()
    {
        var db = await SeedS1Async();
        (await db.Context.MarketInstruments.SingleAsync(i => i.Code == "CURVA 30D")).Active = false;
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var row = (await Service(db).GetGridAsync("26/27", City, Today, CancellationToken.None)).Rows.Single();

        Assert.Equal(MarketPriceStatus.InvalidSetup, row.Status);
    }

    [Fact]
    public async Task Grid_NoSpotConfigured_MissingFx()
    {
        var db = await SeedS1Async(configureSpot: false);

        var row = (await Service(db).GetGridAsync("26/27", City, Today, CancellationToken.None)).Rows.Single();

        Assert.Equal(MarketPriceStatus.MissingFx, row.Status);
        Assert.Null(row.Price);
        Assert.Null(row.SpotFx);
    }

    [Fact]
    public async Task Grid_NoFreightForCity_MissingFreight()
    {
        var db = await SeedS1Async();

        var row = (await Service(db).GetGridAsync("26/27", "3550308", Today, CancellationToken.None)).Rows.Single();

        Assert.Equal(MarketPriceStatus.MissingFreight, row.Status);
        Assert.Null(row.Price);
        Assert.Null(row.FreightPerTon);
        Assert.NotNull(row.PortPrice);
    }

    [Fact]
    public async Task Grid_ExcludesInactiveTablesAndLines_AndOtherHarvests()
    {
        var db = await SeedS1Async();
        var c = db.Context;
        c.HarvestSeasons.Add(new HarvestSeason { Code = "25/26", Name = "Safra 25/26" });
        c.MarketPriceTables.AddRange(
            new MarketPriceTable { Id = 2, Description = "Inativa", ItemCode = "SOJA", ItemName = "SOJA", HarvestSeasonCode = "26/27", Active = false },
            new MarketPriceTable { Id = 3, Description = "Outra safra", ItemCode = "SOJA", ItemName = "SOJA", HarvestSeasonCode = "25/26" });
        await c.SaveChangesAsync();
        foreach (var (table, active) in new[] { (2, true), (3, true), (1, false) })
        {
            var l = S1(); l.PriceTableId = table; l.Active = active;
            c.MarketPriceTableLines.Add(l);
        }
        await c.SaveChangesAsync();
        c.ChangeTracker.Clear();

        var rows = (await Service(db).GetGridAsync("26/27", City, Today, CancellationToken.None)).Rows;

        var only = Assert.Single(rows);
        Assert.Equal(1, only.PriceTableId);
    }

    [Fact]
    public async Task Grid_FutureFreightValidity_NotUsed()
    {
        var db = await SeedS1Async(freight: false);
        db.Context.MarketFreightRates.Add(new MarketFreightRate
        {
            MunicipalityCode = City, DestinationId = 1, RatePerTon = 999, ValidFrom = new DateTime(2026, 12, 1),
        });
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var onlyFuture = (await Service(db).GetGridAsync("26/27", City, Today, CancellationToken.None)).Rows.Single();
        Assert.Equal(MarketPriceStatus.MissingFreight, onlyFuture.Status);

        db.Context.MarketFreightRates.Add(new MarketFreightRate
        {
            MunicipalityCode = City, DestinationId = 1, RatePerTon = 180, ValidFrom = new DateTime(2026, 10, 1),
        });
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var withCurrent = (await Service(db).GetGridAsync("26/27", City, Today, CancellationToken.None)).Rows.Single();
        Assert.Equal(180m, withCurrent.FreightPerTon);
        Assert.Equal(137.67m, withCurrent.Price);
    }

    [Fact]
    public async Task GetRow_ReturnsSameAsGrid()
    {
        var db = await SeedS1Async();
        var service = Service(db);
        var fromGrid = (await service.GetGridAsync("26/27", City, Today, CancellationToken.None)).Rows.Single();

        var row = await service.GetRowAsync(fromGrid.LineId, City, Today, CancellationToken.None);

        Assert.NotNull(row);
        Assert.Equal(fromGrid.Price, row.Price);
        Assert.Equal(fromGrid.NetFx, row.NetFx);
        Assert.Equal(fromGrid.Status, row.Status);
        Assert.Equal(fromGrid.QuotedAt, row.QuotedAt);
        Assert.Equal(fromGrid.Steps, row.Steps);
        Assert.Null(await service.GetRowAsync(9999, City, Today, CancellationToken.None));
    }
}
