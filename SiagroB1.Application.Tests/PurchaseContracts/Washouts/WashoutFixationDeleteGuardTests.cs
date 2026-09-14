using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts.Washouts;

/// <summary>
/// F1 (final review): a FK washout → fixação é Restrict para washout de QUALQUER status.
/// InMemory não enxerga a violação, então a trava tem de vir da Application antes do SQL 547.
/// </summary>
public class WashoutFixationDeleteGuardTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseContractsPriceFixationDeleteService Service() => new(
        _db.Context,
        new PurchaseContractsFixedVolumeService(_db.Context),
        new PurchaseContractsChangeLogService(_db.Context),
        NullLogger<PurchaseContractsPriceFixationDeleteService>.Instance);

    [Fact]
    public async Task Refuses_to_delete_a_fixation_referenced_by_a_rejected_washout()
    {
        var contract = WashoutTestData.Contract(ContractType.ToBeDetermined);
        var fixation = WashoutTestData.Fixation(contract, 30_000m, status: PriceFixationStatus.InApproval);
        var washout = WashoutTestData.Washout(contract, fixation, fixedVolume: 10_000m,
            status: PurchaseContractWashoutStatus.Rejected);

        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsPriceFixations.Add(fixation);
        _db.Context.PurchaseContractsWashouts.Add(washout);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(fixation.Key, "tester"));

        Assert.Contains("washout", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(await _db.Context.PurchaseContractsPriceFixations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == fixation.Key));
    }
}
