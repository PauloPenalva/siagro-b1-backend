using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Models;
using SiagroB1.Fiscal.Taxes;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

public class PurchaseContractsTotalsServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private static PurchaseContract NewContract(decimal totalVolume) => new()
    {
        Key = Guid.NewGuid(),
        Code = "PC-001",
        CardCode = "F0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        HarvestSeasonCode = "24/25",
        DeliveryLocationCode = "01",
        TotalVolume = totalVolume,
    };

    [Fact]
    public async Task GetTotals_SubtractsAllocatedVolumeFromTotalVolume()
    {
        var pc = NewContract(totalVolume: 5000m);
        pc.AllocatedVolume = 5000m; // saldo persistido, sem navegação carregada
        _db.Context.PurchaseContracts.Add(pc);
        await _db.Context.SaveChangesAsync();

        var totals = await Service("SAPB1").GetTotals(pc.Key);

        Assert.Equal(5000m, totals.TotalVolume);
        Assert.Equal(0m, totals.AvaiableVolume);
    }

    private UsageService Usages() => new(_db, NullLogger<UsageService>.Instance);

    private PurchaseContractsTotalsService Service(string mode = "STANDALONE") =>
        new(_db.Context, Usages(), TestConfiguration.Erp(mode));

    /// <summary>Contrato com uma fixação CONFIRMADA de 1.234,567 × 2,345678 → TotalPrice = 2.895,90.</summary>
    private async Task<PurchaseContract> SeedFixedAsync(int? usageCode, bool withTypedTax = false)
    {
        var pc = NewContract(totalVolume: 1234.567m);
        pc.PriceFixations.Add(new PurchaseContractPriceFixation
        {
            Key = Guid.NewGuid(), FixationVolume = 1234.567m, FixationPrice = 2.345678m,
            Status = PriceFixationStatus.Confirmed,
        });
        if (withTypedTax)
        {
            _db.Context.Taxes.Add(new Tax { Code = "ICMS", Name = "ICMS", Rate = 12m });
            pc.Taxes.Add(new PurchaseContractTax { TaxCode = "ICMS" });
        }
        _db.Context.PurchaseContracts.Add(pc);
        if (usageCode is { } code)
            _db.Context.PurchaseContractFiscalComplements.Add(new PurchaseContractFiscalComplement { PurchaseContractKey = pc.Key, UsageCode = code });
        await _db.Context.SaveChangesAsync();
        return pc;
    }

    private async Task<int> SeedUsageAsync(decimal? inss, decimal? rat, decimal? senar, bool inactive = false)
    {
        var model = new UsageModel
        {
            Name = "Compra produtor", Direction = UsageDirection.Incoming, Inactive = inactive,
            CfopIncomingInState = "1102", CfopIncomingOutState = "2102",
            FunruralInssRate = inss, FunruralRatRate = rat, FunruralSenarRate = senar,
        };
        return (await Usages().CreateAsync(model)).Code;
    }

    [Fact]
    public async Task Pf_usage_matches_the_purchase_invoice_calculator()
    {
        var usage = await SeedUsageAsync(1.32m, 0.11m, 0.20m);
        var pc = await SeedFixedAsync(usage);

        var totals = await Service().GetTotals(pc.Key);

        var expected = FunruralCalculator.Calculate(totals.TotalPrice, 1.32m, 0.11m, 0.20m);
        Assert.Equal(2895.90m, totals.FunruralBase);
        Assert.Equal(expected.InssValue, totals.FunruralInssValue);
        Assert.Equal(expected.RatValue, totals.FunruralRatValue);
        Assert.Equal(expected.SenarValue, totals.FunruralSenarValue);
        Assert.Equal(expected.Total, totals.TotalFunrural);
        Assert.Equal(totals.FunruralBase - expected.Total, totals.NetToProducer);
        Assert.Equal(1.32m, totals.FunruralInssRate);
    }

    [Fact]
    public async Task Standalone_total_tax_is_the_funrural_and_ignores_typed_taxes()
    {
        var usage = await SeedUsageAsync(1.32m, 0.11m, 0.20m);
        var pc = await SeedFixedAsync(usage, withTypedTax: true);

        var totals = await Service().GetTotals(pc.Key);

        Assert.Equal(totals.TotalFunrural, totals.TotalTax);
    }

    [Fact]
    public async Task Usage_without_rates_gives_zero_funrural()
    {
        var usage = await SeedUsageAsync(null, null, null);
        var pc = await SeedFixedAsync(usage);

        var totals = await Service().GetTotals(pc.Key);

        Assert.Equal(0m, totals.TotalFunrural);
        Assert.Equal(totals.TotalPrice, totals.FunruralBase);
        Assert.Equal(totals.TotalPrice, totals.NetToProducer);
    }

    [Fact]
    public async Task No_usage_gives_zero_funrural_and_net_equals_base()
    {
        var pc = await SeedFixedAsync(usageCode: null);

        var totals = await Service().GetTotals(pc.Key);

        Assert.Equal(0m, totals.TotalFunrural);
        Assert.Equal(0m, totals.TotalTax);
        Assert.Equal(totals.TotalPrice, totals.NetToProducer);
    }

    [Fact]
    public async Task Inactive_usage_still_feeds_the_totals()
    {
        var usage = await SeedUsageAsync(1.32m, 0.11m, 0.20m, inactive: true);
        var pc = await SeedFixedAsync(usage);

        var totals = await Service().GetTotals(pc.Key);

        Assert.True(totals.TotalFunrural > 0m);
    }

    [Fact]
    public async Task Paf_without_confirmed_fixation_has_zero_base()
    {
        var usage = await SeedUsageAsync(1.32m, 0.11m, 0.20m);
        var pc = NewContract(totalVolume: 1000m);
        _db.Context.PurchaseContracts.Add(pc);
        _db.Context.PurchaseContractFiscalComplements.Add(new PurchaseContractFiscalComplement { PurchaseContractKey = pc.Key, UsageCode = usage });
        await _db.Context.SaveChangesAsync();

        var totals = await Service().GetTotals(pc.Key);

        Assert.Equal(0m, totals.FunruralBase);
        Assert.Equal(0m, totals.TotalFunrural);
        Assert.Equal(0m, totals.NetToProducer);
    }

    [Fact]
    public async Task Sapb1_keeps_the_typed_taxes_and_zero_funrural_fields()
    {
        var pc = await SeedFixedAsync(usageCode: null, withTypedTax: true);

        var totals = await Service("SAPB1").GetTotals(pc.Key);

        Assert.Equal(decimal.Round(2895.90m / 100 * 12m, 2, MidpointRounding.ToEven), totals.TotalTax);
        Assert.Equal(0m, totals.TotalFunrural);
        Assert.Equal(0m, totals.FunruralBase);
        Assert.Equal(0m, totals.NetToProducer);
    }
}
