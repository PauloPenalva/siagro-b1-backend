using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Taxes;

/// <summary>A linha calculada pela natureza — peça comum ao documento de saída e ao de entrada.</summary>
public class TaxLineCalculatorTests
{
    private static async Task<UnitOfWork> SeedAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Items.Add(new Item { ItemCode = "SOJA", ItemName = "SOJA EM GRAOS", Ncm = "12019000", GoodsOrigin = 0 });
        db.Context.IbsCbsRates.Add(new IbsCbsRate { StartDate = new DateOnly(2026, 1, 1), CbsRate = 0.9m, IbsStateRate = 0.1m, IbsMunicipalRate = 0m });
        await db.SaveChangesAsync();
        return db;
    }

    private static UsageModel Purchase() => new()
    {
        Code = 6, Name = "COMPRA DE MERCADORIA", Direction = UsageDirection.Incoming,
        CfopIncomingInState = "1102", CfopIncomingOutState = "2102",
        IcmsInStateCst = "00", IcmsInStateRate = 18m, IcmsOutStateCst = "00",
        PisCst = "74", CofinsCst = "74",
    };

    private static TaxLineRequest Request(UsageModel usage, bool inState = true, string origin = "SP") =>
        new(usage, "SOJA", 1000m, inState, origin, "SP", TaxRegime.Normal, new DateOnly(2026, 10, 5), IncomingCfop: true);

    [Fact]
    public async Task Incoming_cfop_follows_the_state_comparison()
    {
        var db = await SeedAsync();
        var calculator = new TaxLineCalculator(db, new IbsCbsRatesService(db));

        Assert.Equal("1102", (await calculator.CalculateAsync(Request(Purchase()))).Cfop);
        Assert.Equal("2102", (await calculator.CalculateAsync(Request(Purchase(), inState: false, origin: "BA"))).Cfop);
    }

    [Fact]
    public async Task Interstate_entry_rate_comes_from_the_supplier_state()
    {
        var db = await SeedAsync();
        var calculator = new TaxLineCalculator(db, new IbsCbsRatesService(db));

        var result = await calculator.CalculateAsync(Request(Purchase(), inState: false, origin: "BA"));

        Assert.Equal(12m, result.Taxes.IcmsRate);
    }

    [Fact]
    public async Task Missing_incoming_cfop_names_the_kind()
    {
        var db = await SeedAsync();
        var usage = Purchase();
        usage.CfopIncomingInState = null;

        var e = await Assert.ThrowsAsync<DefaultException>(
            () => new TaxLineCalculator(db, new IbsCbsRatesService(db)).CalculateAsync(Request(usage)));

        Assert.Equal("Natureza de operação COMPRA DE MERCADORIA está sem CFOP de entrada dentro do estado.", e.Message);
    }

    [Fact]
    public async Task Line_without_product_is_refused()
    {
        var db = await SeedAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(
            () => new TaxLineCalculator(db, new IbsCbsRatesService(db)).CalculateAsync(Request(Purchase()) with { ItemCode = null }));

        Assert.Equal("Informe o produto do item.", e.Message);
    }

    [Fact]
    public async Task Apply_writes_usage_cfop_product_and_snapshot()
    {
        var db = await SeedAsync();
        var usage = Purchase();
        var result = await new TaxLineCalculator(db, new IbsCbsRatesService(db)).CalculateAsync(Request(usage));
        var line = new PurchaseInvoiceItem { ItemCode = "SOJA", Quantity = 500m, UnitPrice = 2m };

        TaxLineCalculator.Apply(line, usage, result);

        Assert.Equal(6, line.UsageCode);
        Assert.Equal("COMPRA DE MERCADORIA", line.UsageName);
        Assert.Equal("1102", line.Cfop);
        Assert.Equal("12019000", line.Ncm);
        Assert.Equal((byte)0, line.GoodsOrigin);
        Assert.Equal("00", line.CstIcms);
        Assert.Equal(180m, line.IcmsValue);
        Assert.Equal("74", line.CstPis);
    }
}
