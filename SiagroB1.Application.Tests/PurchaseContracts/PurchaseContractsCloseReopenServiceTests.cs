using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

public class PurchaseContractsCloseReopenServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private static PurchaseContract NewContract(ContractStatus status) => new()
    {
        Key = Guid.NewGuid(),
        Code = "PC-001",
        CardCode = "F0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        HarvestSeasonCode = "24/25",
        DeliveryLocationCode = "01",
        TotalVolume = 1000m,
        Status = status,
    };

    private async Task<PurchaseContract> SeedAsync(ContractStatus status)
    {
        var pc = NewContract(status);
        _db.Context.PurchaseContracts.Add(pc);
        await _db.Context.SaveChangesAsync();
        return pc;
    }

    private async Task<PurchaseContract> ReloadAsync(Guid key) =>
        await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == key);

    [Fact]
    public async Task Close_ApprovedContract_BecomesFinished_AndRecordsUser()
    {
        var pc = await SeedAsync(ContractStatus.Approved);

        await new PurchaseContractsCloseService(_db.Context).ExecuteAsync(pc.Key, "paulo.penalva");

        var contract = await ReloadAsync(pc.Key);
        Assert.Equal(ContractStatus.Finished, contract.Status);
        Assert.Equal("paulo.penalva", contract.UpdatedBy);
    }

    [Fact]
    public async Task Close_NonApprovedContract_Throws()
    {
        var pc = await SeedAsync(ContractStatus.Draft);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new PurchaseContractsCloseService(_db.Context).ExecuteAsync(pc.Key, "tester"));
    }

    [Fact]
    public async Task Reopen_FinishedContract_BecomesApproved()
    {
        var pc = await SeedAsync(ContractStatus.Finished);

        await new PurchaseContractsReopenService(_db.Context).ExecuteAsync(pc.Key, "tester");

        Assert.Equal(ContractStatus.Approved, (await ReloadAsync(pc.Key)).Status);
    }

    [Fact]
    public async Task Reopen_NonFinishedContract_Throws()
    {
        var pc = await SeedAsync(ContractStatus.Approved);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new PurchaseContractsReopenService(_db.Context).ExecuteAsync(pc.Key, "tester"));
    }
}
