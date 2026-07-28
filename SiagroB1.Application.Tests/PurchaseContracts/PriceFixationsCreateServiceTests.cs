using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

public class PriceFixationsCreateServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseContractsPriceFixationCreateService Service() =>
        new(_db.Context,
            new PurchaseContractsFixedVolumeService(_db.Context),
            new PurchaseContractsChangeLogService(_db.Context),
            TestNotificationOutbox.For(_db.Context),
            NullLogger<PurchaseContractsPriceFixationCreateService>.Instance);

    private async Task<PurchaseContract> SeedAsync(
        ContractType type = ContractType.ToBeDetermined,
        ContractStatus status = ContractStatus.Approved,
        decimal totalVolume = 100_000m)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC-001",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            DeliveryLocationCode = "01",
            TotalVolume = totalVolume,
            Type = type,
            Status = status,
        };

        _db.Context.PurchaseContracts.Add(contract);
        await _db.Context.SaveChangesAsync();
        return contract;
    }

    private static PurchaseContractPriceFixation Fixation(decimal volume) => new()
    {
        FixationVolume = volume,
        FixationPrice = 2.5m,
        FixationDate = new DateTime(2026, 7, 20),
    };

    [Fact]
    public async Task Create_WithinBalance_PersistsAsInApproval_AndUpdatesFixedVolume()
    {
        var contract = await SeedAsync();

        var created = await Service().ExecuteAsync(contract.Key, Fixation(30_000m), "operador");

        Assert.Equal(PriceFixationStatus.InApproval, created.Status);
        Assert.Equal("operador", created.CreatedBy);
        Assert.Equal(30_000m, contract.FixedVolume);
        Assert.Equal(70_000m, contract.AvailableVolumeToPricing);
    }

    [Fact]
    public async Task Create_ExactlyConsumingBalance_IsAllowed()
    {
        var contract = await SeedAsync();

        await Service().ExecuteAsync(contract.Key, Fixation(100_000m), "operador");

        Assert.Equal(0m, contract.AvailableVolumeToPricing);
    }

    [Fact]
    public async Task Create_ExceedingBalance_Throws()
    {
        var contract = await SeedAsync();
        await Service().ExecuteAsync(contract.Key, Fixation(80_000m), "operador");

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            Service().ExecuteAsync(contract.Key, Fixation(30_000m), "operador"));

        Assert.Contains("saldo", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_OnFixedContract_Throws()
    {
        var contract = await SeedAsync(type: ContractType.Fixed);

        await Assert.ThrowsAsync<ApplicationException>(() =>
            Service().ExecuteAsync(contract.Key, Fixation(10_000m), "operador"));
    }

    [Fact]
    public async Task Create_OnFinishedContract_Throws()
    {
        var contract = await SeedAsync(status: ContractStatus.Finished);

        await Assert.ThrowsAsync<ApplicationException>(() =>
            Service().ExecuteAsync(contract.Key, Fixation(10_000m), "operador"));
    }

    [Fact]
    public async Task Create_WithNonPositiveVolume_Throws()
    {
        var contract = await SeedAsync();

        await Assert.ThrowsAsync<ApplicationException>(() =>
            Service().ExecuteAsync(contract.Key, Fixation(0m), "operador"));
    }

    [Fact]
    public async Task Create_SecondFixation_AccumulatesFixedVolume()
    {
        var contract = await SeedAsync();

        await Service().ExecuteAsync(contract.Key, Fixation(30_000m), "operador");
        await Service().ExecuteAsync(contract.Key, Fixation(25_000m), "operador");

        Assert.Equal(55_000m, contract.FixedVolume);
        Assert.Equal(45_000m, contract.AvailableVolumeToPricing);
    }
}
