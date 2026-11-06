using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using static SiagroB1.Application.Tests.Market.MarketTestHelpers;

namespace SiagroB1.Application.Tests.Market;

public class MarketPricingModelTests
{
    /// <summary>Contexto sem rastreamento anterior: força a leitura do que foi gravado.</summary>
    private static SiagroB1.Infra.Context.AppDbContext Fresh(SiagroB1.Infra.UnitOfWork db)
    {
        db.Context.ChangeTracker.Clear();
        return db.Context;
    }

    [Fact]
    public async Task PriceTable_PersistsWithTwoLines()
    {
        var db = await SeedAsync(Instrument("ZSF27"), Instrument("USDFUT"));
        var ctx = db.Context;
        ctx.HarvestSeasons.Add(new HarvestSeason { Code = "2627", Name = "Safra 26/27" });
        var destination = new MarketDestination { Name = "Paranaguá" };
        ctx.MarketDestinations.Add(destination);
        await ctx.SaveChangesAsync();

        ctx.MarketPriceTables.Add(new MarketPriceTable
        {
            Description = "Soja 26/27",
            ItemCode = "SOJA",
            ItemName = "Soja em grãos",
            HarvestSeasonCode = "2627",
            Lines =
            [
                new() { Description = "Mar/27", InstrumentCode = "ZSF27", FxCurveInstrumentCode = "USDFUT", DestinationId = destination.Id, DailyInterestPercent = 0.033m, SortOrder = 1 },
                new() { Description = "Abr/27", InstrumentCode = "ZSF27", DestinationId = destination.Id, DiscountIsPercent = true, DeliveryStartDate = new DateTime(2027, 4, 1), SortOrder = 2 },
            ],
        });
        await ctx.SaveChangesAsync();

        var read = await Fresh(db).MarketPriceTables.Include(t => t.Lines).SingleAsync();
        Assert.Equal(2, read.Lines.Count);
        Assert.Contains(read.Lines, l => l.FxCurveInstrumentCode == "USDFUT" && l.DailyInterestPercent == 0.033m);
        Assert.Contains(read.Lines, l => l.FxCurveInstrumentCode == null && l.DiscountIsPercent);
    }

    [Fact]
    public async Task FreightRates_AllowSameCityAndDestinationWithDifferentValidFrom()
    {
        var db = TestDb.CreateUnitOfWork();
        var ctx = db.Context;
        ctx.Municipalities.Add(new Municipality { Code = "4104808", Name = "Cascavel", StateAbbreviation = "PR" });
        var destination = new MarketDestination { Name = "Paranaguá" };
        ctx.MarketDestinations.Add(destination);
        await ctx.SaveChangesAsync();

        ctx.MarketFreightRates.AddRange(
            new MarketFreightRate { MunicipalityCode = "4104808", DestinationId = destination.Id, RatePerTon = 180m, ValidFrom = new DateTime(2026, 10, 1), CreatedBy = "ana", CreatedAt = DateTime.UtcNow },
            new MarketFreightRate { MunicipalityCode = "4104808", DestinationId = destination.Id, RatePerTon = 190m, ValidFrom = new DateTime(2026, 11, 1), CreatedBy = "ana", CreatedAt = DateTime.UtcNow });
        await ctx.SaveChangesAsync();

        Assert.Equal(2, await Fresh(db).MarketFreightRates.CountAsync());
    }

    [Fact]
    public async Task TradingSettings_SingletonReadsAndWrites()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.MarketTradingSettings.Add(new MarketTradingSettings
        {
            SpotFxInstrumentCode = "USDSPOT",
            BoardApprovalVolumeTons = 500m,
            BoardApprovalDeviationPercent = 1.5m,
            ContractUnitOfMeasureCode = "KG",
            ContractFreightUmCode = "TON",
        });
        await db.SaveChangesAsync();

        var read = await Fresh(db).MarketTradingSettings.SingleAsync();
        Assert.Equal(1, read.Key);
        Assert.Equal(500m, read.BoardApprovalVolumeTons);
        Assert.Equal("KG", read.ContractUnitOfMeasureCode);
    }

    [Fact]
    public async Task TradeTicket_PersistsFrozenInputs()
    {
        var db = TestDb.CreateUnitOfWork();
        var ctx = db.Context;
        ctx.HarvestSeasons.Add(new HarvestSeason { Code = "2627", Name = "Safra 26/27" });
        ctx.Municipalities.Add(new Municipality { Code = "4104808", Name = "Cascavel", StateAbbreviation = "PR" });
        await ctx.SaveChangesAsync();

        ctx.MarketTradeTickets.Add(new MarketTradeTicket
        {
            Status = MarketTradeTicketStatus.PendingBoard,
            CreatedBy = "ana",
            CreatedAt = DateTime.UtcNow,
            PriceTableId = 99, PriceTableLineId = 98, DestinationId = 97, InstrumentCode = "ZSF27",
            PriceTableDescription = "Soja 26/27", LineDescription = "Mar/27",
            ItemCode = "SOJA", ItemName = "Soja", HarvestSeasonCode = "2627", MunicipalityCode = "4104808", DestinationName = "Paranaguá",
            ExchangeQuote = 1188.25m, QuoteSource = MarketQuoteSource.Cma, CalculatedPrice = 137.67m, StepsJson = "[]",
            CardCode = "F001", CardName = "Fornecedor", VolumeKg = 30000m, NegotiatedPrice = 137m,
        });
        await ctx.SaveChangesAsync();

        var read = await Fresh(db).MarketTradeTickets.SingleAsync();
        Assert.True(read.Id > 0);
        Assert.Equal(MarketTradeTicketStatus.PendingBoard, read.Status);
        Assert.Equal(137.67m, read.CalculatedPrice);
        Assert.Null(read.PurchaseContractKey);
    }
}
