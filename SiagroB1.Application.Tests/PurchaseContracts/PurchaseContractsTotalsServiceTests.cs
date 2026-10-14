using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
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

        var totals = await new PurchaseContractsTotalsService(_db.Context).GetTotals(pc.Key);

        Assert.Equal(5000m, totals.TotalVolume);
        Assert.Equal(0m, totals.AvaiableVolume);
    }
}
