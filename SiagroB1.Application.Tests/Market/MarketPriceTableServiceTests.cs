using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Exceptions;
using static SiagroB1.Application.Tests.Market.MarketPricingTestHelpers;

namespace SiagroB1.Application.Tests.Market;

public class MarketPriceTableServiceTests
{
    [Fact]
    public async Task PriceTable_UnknownItem_Rejected()
    {
        var db = await SeedPricingAsync();

        await Assert.ThrowsAsync<BusinessException>(() => TableService(db).CreateAsync(
            new MarketPriceTable { Description = "x", ItemCode = "XXX", ItemName = "", HarvestSeasonCode = "26/27" }, "ana"));
    }

    [Fact]
    public async Task PriceTable_Create_CopiesItemNameAndStamps()
    {
        var db = await SeedPricingAsync();

        var created = await TableService(db).CreateAsync(
            new MarketPriceTable { Description = "Soja 26/27", ItemCode = "SOJA", ItemName = "", HarvestSeasonCode = "26/27" }, "ana");

        Assert.Equal("SOJA EM GRAOS", created.ItemName);
        Assert.Equal("ana", created.UpdatedBy);
        Assert.NotEqual(default, created.UpdatedAt);
    }

    [Fact]
    public async Task PriceTableLine_UsdInstrumentWithoutCurve_Rejected()
    {
        var db = await SeedPricingAsync(Usd(), Curve());

        var ex = await Assert.ThrowsAsync<BusinessException>(() => LineService(db).CreateAsync(Line("ZSF7"), "ana"));
        Assert.Contains("curva", ex.Message);
    }

    [Fact]
    public async Task PriceTableLine_CurveWithoutDays_Rejected()
    {
        var curve = Curve();
        curve.CurveDays = null;
        var db = await SeedPricingAsync(Usd(), curve);

        await Assert.ThrowsAsync<BusinessException>(() => LineService(db).CreateAsync(Line("ZSF7", "CURVA 30D"), "ana"));
    }

    [Fact]
    public async Task PriceTableLine_UsdInstrumentWithCurve_Saved()
    {
        var db = await SeedPricingAsync(Usd(), Curve());

        await LineService(db).CreateAsync(Line("ZSF7", "CURVA 30D"), "ana");

        Assert.Equal("CURVA 30D", (await db.Context.MarketPriceTableLines.AsNoTracking().SingleAsync()).FxCurveInstrumentCode);
    }

    [Fact]
    public async Task PriceTableLine_BrlInstrument_NoCurveNeeded_AndCurveIgnored()
    {
        var db = await SeedPricingAsync(Brl(), Curve());

        await LineService(db).CreateAsync(Line("CCMF27", "CURVA 30D"), "ana");

        Assert.Null((await db.Context.MarketPriceTableLines.AsNoTracking().SingleAsync()).FxCurveInstrumentCode);
    }

    [Fact]
    public async Task PriceTableLine_DollarInstrumentAsCommodity_Rejected()
    {
        var db = await SeedPricingAsync(SpotFx());

        await Assert.ThrowsAsync<BusinessException>(() => LineService(db).CreateAsync(Line("DOL COM"), "ana"));
    }

    [Fact]
    public async Task PriceTableLine_MissingDatesOrBadRanges_Rejected()
    {
        var db = await SeedPricingAsync(Brl());
        var service = LineService(db);
        var noPayment = Line("CCMF27");
        noPayment.PaymentDate = null;
        var noReceipt = Line("CCMF27");
        noReceipt.ReceiptDate = null;
        var badDelivery = Line("CCMF27");
        badDelivery.DeliveryStartDate = new DateTime(2027, 2, 1);
        badDelivery.DeliveryEndDate = new DateTime(2027, 1, 1);
        var badPercent = Line("CCMF27");
        badPercent.MarginPercent = 101m;

        foreach (var line in new[] { noPayment, noReceipt, badDelivery, badPercent })
            await Assert.ThrowsAsync<BusinessException>(() => service.CreateAsync(line, "ana"));
    }

    [Fact]
    public async Task PriceTableLine_InactiveInstrumentOrDestination_Rejected()
    {
        var inactive = Brl();
        inactive.Active = false;
        var db = await SeedPricingAsync(inactive, Brl("OUTRO"));
        var service = LineService(db);
        await Assert.ThrowsAsync<BusinessException>(() => service.CreateAsync(Line("CCMF27"), "ana"));

        var destination = await db.Context.MarketDestinations.SingleAsync();
        destination.Active = false;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessException>(() => service.CreateAsync(Line("OUTRO"), "ana"));
    }

    [Fact]
    public async Task PriceTableLine_Write_StampsHeader()
    {
        var db = await SeedPricingAsync(Brl());
        var created = await LineService(db).CreateAsync(Line("CCMF27"), "carla");

        var header = await db.Context.MarketPriceTables.AsNoTracking().SingleAsync();
        Assert.Equal("carla", header.UpdatedBy);

        await LineService(db).DeleteAsync(created.Id, "davi");
        db.Context.ChangeTracker.Clear();
        Assert.Equal("davi", (await db.Context.MarketPriceTables.AsNoTracking().SingleAsync()).UpdatedBy);
        Assert.Empty(db.Context.MarketPriceTableLines);
    }

    [Fact]
    public async Task Instrument_DeleteUsedByLine_Rejected()
    {
        var db = await SeedPricingAsync(Brl());
        await LineService(db).CreateAsync(Line("CCMF27"), "ana");

        await Assert.ThrowsAsync<BusinessException>(() => MarketTestHelpers.InstrumentService(db).DeleteAsync("CCMF27"));
    }
}
