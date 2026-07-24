using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesContracts;

public class SalesContractsDeliveryLocationsCreateServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();
    private readonly FakeBusinessPartnerService _partners =
        new(new() { ["C0001"] = "Terminal Santos", ["C0002"] = "Terminal Paranagua" });

    private SalesContractsDeliveryLocationsCreateService Service() =>
        new(_db.Context, _partners, new SalesContractsChangeLogService(_db.Context),
            NullLogger<SalesContractsDeliveryLocationsCreateService>.Instance);

    private async Task<SalesContract> SeedContractAsync()
    {
        var contract = new SalesContract
        {
            Key = Guid.NewGuid(),
            Code = "SC-001",
            CardCode = "C9999",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            Status = ContractStatus.Draft,
        };
        _db.Context.SalesContracts.Add(contract);
        await _db.Context.SaveChangesAsync();
        return contract;
    }

    [Fact]
    public async Task Create_ResolvesCardName_AndLinksToContract()
    {
        var contract = await SeedContractAsync();

        var created = await Service().ExecuteAsync(contract.Key,
            new SalesContractDeliveryLocation { CardCode = "C0001" }, "tester");

        Assert.Equal("Terminal Santos", created.CardName);
        Assert.Equal(contract.Key, created.SalesContractKey);
    }

    [Fact]
    public async Task Create_DuplicateCardCodeInSameContract_ThrowsDefaultException()
    {
        var contract = await SeedContractAsync();
        await Service().ExecuteAsync(contract.Key,
            new SalesContractDeliveryLocation { CardCode = "C0001" }, "tester");

        await Assert.ThrowsAsync<DefaultException>(() =>
            Service().ExecuteAsync(contract.Key,
                new SalesContractDeliveryLocation { CardCode = "C0001" }, "tester"));
    }

    [Fact]
    public async Task Create_ContractNotFound_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Service().ExecuteAsync(Guid.NewGuid(),
                new SalesContractDeliveryLocation { CardCode = "C0001" }, "tester"));
    }
}
