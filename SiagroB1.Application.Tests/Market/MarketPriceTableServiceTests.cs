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
    public async Task PriceTable_ItemNameOptionalOnInput_FilledByServer_ColumnStillRequired()
    {
        var db = await SeedPricingAsync();

        var created = await TableService(db).CreateAsync(
            new MarketPriceTable { Description = "Soja", ItemCode = "SOJA", HarvestSeasonCode = "26/27" }, "ana");

        Assert.Equal("SOJA EM GRAOS", created.ItemName);
        // Anulável no CLR (o MVC não exige do cliente), obrigatória no modelo do EF (coluna NOT NULL, sem migration).
        var nullability = new System.Reflection.NullabilityInfoContext()
            .Create(typeof(MarketPriceTable).GetProperty(nameof(MarketPriceTable.ItemName))!);
        Assert.Equal(System.Reflection.NullabilityState.Nullable, nullability.WriteState);
        Assert.False(db.Context.Model.FindEntityType(typeof(MarketPriceTable))!
            .FindProperty(nameof(MarketPriceTable.ItemName))!.IsNullable);
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

    private static MarketPriceTable NewTable(params MarketPriceTableLine[] lines) => new()
    {
        Description = "Nova", ItemCode = "SOJA", ItemName = "", HarvestSeasonCode = "26/27", Lines = [.. lines],
    };

    [Fact]
    public async Task DeepInsert_ValidLine_Saved_AndNormalised()
    {
        var db = await SeedPricingAsync(Brl(), Curve());

        await TableService(db).CreateAsync(NewTable(Line("CCMF27", "CURVA 30D")), "ana");

        var line = await db.Context.MarketPriceTableLines.AsNoTracking().SingleAsync(l => l.PriceTableId != 1);
        Assert.Null(line.FxCurveInstrumentCode);
        Assert.Equal(2, await db.Context.MarketPriceTables.CountAsync());
    }

    [Fact]
    public async Task DeepInsert_OneInvalidLine_Rejected_NothingSaved()
    {
        var db = await SeedPricingAsync(Brl(), Usd(), Curve());
        var valid = Line("CCMF27");
        var usdWithoutCurve = Line("ZSF7");

        await Assert.ThrowsAsync<BusinessException>(() =>
            TableService(db).CreateAsync(NewTable(valid, usdWithoutCurve), "ana"));

        db.Context.ChangeTracker.Clear();
        Assert.Equal(1, await db.Context.MarketPriceTables.CountAsync());
        Assert.Empty(db.Context.MarketPriceTableLines);
    }

    [Fact]
    public async Task HeaderUpdate_TrackedEntity_KeepsTrackedLines_IgnoresPayloadLines()
    {
        var db = await SeedPricingAsync(Brl());
        await LineService(db).CreateAsync(Line("CCMF27"), "ana");
        db.Context.ChangeTracker.Clear();

        var header = await db.Context.MarketPriceTables.Include(t => t.Lines).SingleAsync();
        header.Description = "Renomeada";
        header.Lines.Add(new MarketPriceTableLine { Description = "x", InstrumentCode = "CCMF27" });
        await TableService(db).UpdateAsync(header.Id, header, "bia");

        db.Context.ChangeTracker.Clear();
        Assert.Single(db.Context.MarketPriceTableLines);
        Assert.Equal("Renomeada", (await db.Context.MarketPriceTables.SingleAsync()).Description);
    }

    [Fact]
    public async Task LineUpdate_MovingToAnotherTable_Rejected()
    {
        var db = await SeedPricingAsync(Brl());
        var created = await LineService(db).CreateAsync(Line("CCMF27"), "ana");
        db.Context.MarketPriceTables.Add(new MarketPriceTable
        {
            Id = 2, Description = "Outra", ItemCode = "SOJA", ItemName = "SOJA", HarvestSeasonCode = "26/27",
        });
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var moved = Line("CCMF27");
        moved.PriceTableId = 2;
        await Assert.ThrowsAsync<BusinessException>(() => LineService(db).UpdateAsync(created.Id, moved, "ana"));
    }
}
