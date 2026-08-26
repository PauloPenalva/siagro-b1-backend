using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesShipmentReleases;

public class SalesShipmentReleasesGetAvailableServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesShipmentReleasesGetAvailableService Service() => new(_db);

    private async Task SeedAsync(
        string itemCode, ReleaseStatus status, decimal released, decimal shipped, decimal price = 50m,
        ContractStatus contractStatus = ContractStatus.Approved, decimal allocatedVolume = 0m)
    {
        var sc = new SalesContract
        {
            Key = Guid.NewGuid(), Code = "SC", CardCode = "C0001", CardName = "Cliente",
            ItemCode = itemCode, ItemName = itemCode, UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25", TotalVolume = 10000m, Price = price,
            Status = contractStatus, AllocatedVolume = allocatedVolume,
        };
        _db.Context.SalesContracts.Add(sc);
        _db.Context.SalesShipmentReleases.Add(new SalesShipmentRelease
        {
            Key = Guid.NewGuid(), SalesContractKey = sc.Key, DeliveryLocationCode = "01",
            ReleasedQuantity = released, ShippedQuantity = shipped, Status = status,
        });
        await _db.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task Query_ReturnsOnlyActivedWithBalanceForItem()
    {
        await SeedAsync("SOJA", ReleaseStatus.Actived, released: 1000m, shipped: 200m);   // ok (saldo 800)
        await SeedAsync("SOJA", ReleaseStatus.Actived, released: 500m, shipped: 500m);    // sem saldo
        await SeedAsync("SOJA", ReleaseStatus.Pending, released: 1000m, shipped: 0m);     // não ativa
        await SeedAsync("MILHO", ReleaseStatus.Actived, released: 1000m, shipped: 0m);    // outro produto

        var result = Service().Query("SOJA").ToList();

        Assert.Single(result);
        Assert.Equal(800m, result[0].AvailableQuantity);
        Assert.Equal("SOJA", result[0].ItemCode);
        Assert.Equal(50m, result[0].Price);
        Assert.Equal("C0001", result[0].CardCode);
    }
[Fact]
    public async Task Query_HidesContractsWithoutBalanceByDefault()
    {
        await SeedAsync("SOJA", ReleaseStatus.Actived, released: 1000m, shipped: 0m);                       // saldo 10000
        await SeedAsync("SOJA", ReleaseStatus.Actived, released: 1000m, shipped: 0m, allocatedVolume: 10000m); // saldo 0
        await SeedAsync("SOJA", ReleaseStatus.Actived, released: 1000m, shipped: 0m, allocatedVolume: 12000m); // negativo

        var result = Service().Query("SOJA").ToList();

        Assert.Single(result);
    }

    [Fact]
    public async Task Query_CanIncludeContractsWithoutBalance()
    {
        // O switch é o escape que impede o filtro de virar a trava que a decisão de
        // 21/08/2026 tirou do serviço: sem guard de saldo, esconder o contrato só levaria o
        // usuário a criar um contrato "AJUSTE DE SALDO".
        await SeedAsync("SOJA", ReleaseStatus.Actived, released: 1000m, shipped: 0m);
        await SeedAsync("SOJA", ReleaseStatus.Actived, released: 1000m, shipped: 0m, allocatedVolume: 10000m);
        await SeedAsync("SOJA", ReleaseStatus.Actived, released: 1000m, shipped: 0m, allocatedVolume: 12000m);

        var result = Service().Query("SOJA", includeContractsWithoutBalance: true).ToList();

        Assert.Equal(3, result.Count);
        Assert.Contains(result, r => r.SalesContractAvailableVolume < decimal.Zero);
    }

    [Fact]
    public async Task Query_NeverShowsContractsThatAreNotApproved()
    {
        await SeedAsync("SOJA", ReleaseStatus.Actived, released: 1000m, shipped: 0m, contractStatus: ContractStatus.Draft);
        await SeedAsync("SOJA", ReleaseStatus.Actived, released: 1000m, shipped: 0m, contractStatus: ContractStatus.Finished);

        // O parâmetro afrouxa APENAS a cláusula de saldo; status continua valendo nos dois casos.
        Assert.Empty(Service().Query("SOJA").ToList());
        Assert.Empty(Service().Query("SOJA", includeContractsWithoutBalance: true).ToList());
    }

    [Fact]
    public async Task Query_StillFiltersTheReleaseBalanceWhenIncludingContractsWithoutBalance()
    {
        // Eixo diferente: o switch é sobre o saldo do CONTRATO, não o da liberação.
        await SeedAsync("SOJA", ReleaseStatus.Actived, released: 500m, shipped: 500m, allocatedVolume: 10000m);

        Assert.Empty(Service().Query("SOJA", includeContractsWithoutBalance: true).ToList());
    }

    [Fact]
    public async Task Query_IncludesCommercialPriceWhenItemHasCommercialUnitOfMeasureConfigured()
    {
        await SeedAsync("SOJA", ReleaseStatus.Actived, released: 1000m, shipped: 0m, price: 50m);
        _db.Context.ItemComplements.Add(new ItemComplement
        {
            ItemCode = "SOJA", CommercialUnitOfMeasureCode = "SC", CommercialFactor = 60m,
        });
        await _db.Context.SaveChangesAsync();

        var result = Service().Query("SOJA").ToList();

        Assert.Single(result);
        Assert.Equal("SC", result[0].CommercialUnitOfMeasureCode);
        Assert.Equal(3000m, result[0].CommercialPrice);
    }

    /// <summary>
    /// Complemento pela metade nao converte: sigla comercial com preco em KG ao lado seria um par
    /// inconsistente, pior que simplesmente cair para KG.
    /// </summary>
    [Fact]
    public async Task Query_CommercialFieldsAreNullWhenTheComplementIsHalfFilled()
    {
        await SeedAsync("SOJA", ReleaseStatus.Actived, released: 1000m, shipped: 0m, price: 50m);
        _db.Context.ItemComplements.Add(new ItemComplement
        {
            ItemCode = "SOJA", CommercialUnitOfMeasureCode = "SC", CommercialFactor = null,
        });
        await _db.Context.SaveChangesAsync();

        var result = Service().Query("SOJA").ToList();

        Assert.Null(result[0].CommercialUnitOfMeasureCode);
        Assert.Null(result[0].CommercialPrice);
    }

    [Fact]
    public async Task Query_CommercialFieldsAreNullWhenItemHasNoCommercialUnitOfMeasureConfigured()
    {
        await SeedAsync("SOJA", ReleaseStatus.Actived, released: 1000m, shipped: 0m, price: 50m);

        var result = Service().Query("SOJA").ToList();

        Assert.Single(result);
        Assert.Null(result[0].CommercialUnitOfMeasureCode);
        Assert.Null(result[0].CommercialPrice);
        Assert.Equal(50m, result[0].Price);
    }
}
