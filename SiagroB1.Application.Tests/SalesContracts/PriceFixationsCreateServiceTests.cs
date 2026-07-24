using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesContracts;

public class PriceFixationsCreateServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesContractsPriceFixationCreateService Service() =>
        new(_db.Context,
            new SalesContractsFixedVolumeService(_db.Context),
            new SalesContractsChangeLogService(_db.Context),
            NullLogger<SalesContractsPriceFixationCreateService>.Instance);

    private async Task<SalesContract> SeedAsync(
        ContractType type = ContractType.ToBeDetermined,
        ContractStatus status = ContractStatus.Approved,
        decimal totalVolume = 100_000m)
    {
        var contract = new SalesContract
        {
            Key = Guid.NewGuid(),
            Code = "SC-001",
            CardCode = "C0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            TotalVolume = totalVolume,
            Type = type,
            Status = status,
        };

        _db.Context.SalesContracts.Add(contract);
        await _db.Context.SaveChangesAsync();
        return contract;
    }

    private static SalesContractPriceFixation Fixation(decimal volume) => new()
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
