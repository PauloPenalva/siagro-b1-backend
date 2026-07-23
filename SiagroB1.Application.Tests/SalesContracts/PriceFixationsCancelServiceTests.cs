using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesContracts;

public class PriceFixationsCancelServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesContractsPriceFixationsCancelService Service() =>
        new(_db.Context, new SalesContractsFixedVolumeService(_db.Context));

    private async Task<(SalesContract Contract, SalesContractPriceFixation Fixation)> SeedAsync(
        PriceFixationStatus status = PriceFixationStatus.Confirmed,
        ContractStatus contractStatus = ContractStatus.Approved)
    {
        var contract = new SalesContract
        {
            Key = Guid.NewGuid(),
            Code = "SC-001",
            CardCode = "C0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            TotalVolume = 100_000m,
            FixedVolume = 40_000m,
            Type = ContractType.ToBeDetermined,
            Status = contractStatus,
        };

        var fixation = new SalesContractPriceFixation
        {
            Key = Guid.NewGuid(),
            SalesContractKey = contract.Key,
            FixationVolume = 40_000m,
            FixationPrice = 2.5m,
            Status = status,
        };

        _db.Context.SalesContracts.Add(contract);
        _db.Context.SalesContractsPriceFixations.Add(fixation);
        await _db.Context.SaveChangesAsync();

        return (contract, fixation);
    }

    [Fact]
    public async Task Cancel_ConfirmedFixation_ReturnsToInApproval_AndClearsApproval()
    {
        var (_, fixation) = await SeedAsync();

        await Service().ExecuteAsync(fixation.Key, "operador");

        var reloaded = await _db.Context.SalesContractsPriceFixations
            .AsNoTracking().SingleAsync(x => x.Key == fixation.Key);

        Assert.Equal(PriceFixationStatus.InApproval, reloaded.Status);
        Assert.True(string.IsNullOrEmpty(reloaded.ApprovedBy));
        Assert.Null(reloaded.ApprovedAt);
        Assert.Null(reloaded.ApprovalComments);
        Assert.Equal("operador", reloaded.CanceledBy);
        Assert.NotNull(reloaded.CanceledAt);
    }

    [Fact]
    public async Task Cancel_KeepsVolumeReserved()
    {
        var (contract, fixation) = await SeedAsync();

        await Service().ExecuteAsync(fixation.Key, "operador");

        var reloaded = await _db.Context.SalesContracts
            .AsNoTracking().SingleAsync(x => x.Key == contract.Key);

        // InApproval reserva volume igual a Confirmed: estornar NÃO devolve saldo.
        Assert.Equal(40_000m, reloaded.FixedVolume);
        Assert.Equal(60_000m, reloaded.AvailableVolumeToPricing);
    }

    [Fact]
    public async Task Cancel_RemovesFixationFromTotalPrice()
    {
        var (contract, fixation) = await SeedAsync();

        await Service().ExecuteAsync(fixation.Key, "operador");

        var reloaded = await _db.Context.SalesContracts
            .Include(x => x.PriceFixations)
            .AsNoTracking().SingleAsync(x => x.Key == contract.Key);

        Assert.Equal(0m, reloaded.TotalPrice);
    }

    [Fact]
    public async Task Cancel_InApprovalFixation_Throws()
    {
        var (_, fixation) = await SeedAsync(status: PriceFixationStatus.InApproval);

        await Assert.ThrowsAsync<ApplicationException>(() =>
            Service().ExecuteAsync(fixation.Key, "operador"));
    }

    [Fact]
    public async Task Cancel_AlreadyCanceled_Throws()
    {
        var (_, fixation) = await SeedAsync(status: PriceFixationStatus.Canceled);

        await Assert.ThrowsAsync<ApplicationException>(() =>
            Service().ExecuteAsync(fixation.Key, "operador"));
    }

    [Fact]
    public async Task Cancel_OnFinishedContract_Throws()
    {
        var (_, fixation) = await SeedAsync(contractStatus: ContractStatus.Finished);

        await Assert.ThrowsAsync<ApplicationException>(() =>
            Service().ExecuteAsync(fixation.Key, "operador"));
    }

    [Fact]
    public async Task Cancel_ThenReapprove_RestoresTotalPrice()
    {
        var (contract, fixation) = await SeedAsync();

        await Service().ExecuteAsync(fixation.Key, "operador");

        var afterCancel = await _db.Context.SalesContracts
            .Include(x => x.PriceFixations)
            .AsNoTracking().SingleAsync(x => x.Key == contract.Key);
        Assert.Equal(0m, afterCancel.TotalPrice);

        await new SalesContractsPriceFixationsApprovalService(
                _db.Context, new SalesContractsFixedVolumeService(_db.Context))
            .ExecuteAsync(fixation.Key, "reaprovado", "diretoria");

        var afterReapproval = await _db.Context.SalesContracts
            .Include(x => x.PriceFixations)
            .AsNoTracking().SingleAsync(x => x.Key == contract.Key);

        // 40.000 × 2,50 = 100.000
        Assert.Equal(100_000m, afterReapproval.TotalPrice);
    }
}
