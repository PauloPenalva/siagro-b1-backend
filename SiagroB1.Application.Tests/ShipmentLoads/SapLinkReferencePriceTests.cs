using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

public class SapLinkReferencePriceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesContract NewContract(decimal price, CurrencyType? currency = CurrencyType.Brl) => new()
    {
        Key = Guid.NewGuid(),
        Code = "SC-001",
        CardCode = "C0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        HarvestSeasonCode = "24/25",
        TotalVolume = 100_000m,
        Type = ContractType.Fixed,
        Status = ContractStatus.Approved,
        Price = price,
        StandardCurrency = currency,
    };

    private Task<decimal?> ResolveAsync(SalesContract c) =>
        SapLinkReferencePrice.ResolveAsync(new SalesContractsFixedVolumeService(_db.Context), c);

    [Fact]
    public async Task Dollar_contract_has_no_reference_price() =>
        Assert.Null(await ResolveAsync(NewContract(2m, CurrencyType.Usd)));

    [Fact]
    public async Task Contract_without_confirmed_price_has_no_reference_price() =>
        Assert.Null(await ResolveAsync(NewContract(0m)));

    [Fact]
    public async Task Fixed_contract_uses_its_price() =>
        Assert.Equal(2.5m, await ResolveAsync(NewContract(2.5m, null)));

    [Fact]
    public async Task Confirmed_fixations_give_the_weighted_price()
    {
        var c = NewContract(0m);
        _db.Context.SalesContracts.Add(c);
        _db.Context.SalesContractsPriceFixations.AddRange(
            new SalesContractPriceFixation { Key = Guid.NewGuid(), SalesContractKey = c.Key, FixationVolume = 30_000m, FixationPrice = 2m, Status = PriceFixationStatus.Confirmed },
            new SalesContractPriceFixation { Key = Guid.NewGuid(), SalesContractKey = c.Key, FixationVolume = 10_000m, FixationPrice = 6m, Status = PriceFixationStatus.Confirmed });
        await _db.Context.SaveChangesAsync();

        Assert.Equal(3m, await ResolveAsync(c));
    }
}
