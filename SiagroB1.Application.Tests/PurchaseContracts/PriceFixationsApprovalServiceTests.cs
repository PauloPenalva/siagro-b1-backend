using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

public class PriceFixationsApprovalServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseContractsFixedVolumeService FixedVolume() => new(_db.Context);

    private async Task<(PurchaseContract Contract, PurchaseContractPriceFixation Fixation)> SeedAsync(
        PriceFixationStatus status = PriceFixationStatus.InApproval,
        ContractStatus contractStatus = ContractStatus.Approved)
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
            TotalVolume = 100_000m,
            FixedVolume = 30_000m,
            Type = ContractType.ToBeDetermined,
            Status = contractStatus,
        };

        var fixation = new PurchaseContractPriceFixation
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            FixationVolume = 30_000m,
            FixationPrice = 2.5m,
            Status = status,
        };

        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsPriceFixations.Add(fixation);
        await _db.Context.SaveChangesAsync();

        return (contract, fixation);
    }

    private async Task<PurchaseContractPriceFixation> ReloadFixationAsync(Guid key) =>
        await _db.Context.PurchaseContractsPriceFixations.AsNoTracking().SingleAsync(x => x.Key == key);

    private async Task<PurchaseContract> ReloadContractAsync(Guid key) =>
        await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == key);

    [Fact]
    public async Task Approve_InApprovalFixation_BecomesConfirmed_AndRecordsApprover()
    {
        var (_, fixation) = await SeedAsync();

        await new PurchaseContractsPriceFixationsApprovalService(_db.Context, FixedVolume())
            .ExecuteAsync(fixation.Key, "aprovado em reunião", "diretoria");

        var reloaded = await ReloadFixationAsync(fixation.Key);
        Assert.Equal(PriceFixationStatus.Confirmed, reloaded.Status);
        Assert.Equal("diretoria", reloaded.ApprovedBy);
        Assert.Equal("aprovado em reunião", reloaded.ApprovalComments);
        Assert.NotNull(reloaded.ApprovedAt);
    }

    [Fact]
    public async Task Approve_KeepsFixedVolumeUnchanged()
    {
        var (contract, fixation) = await SeedAsync();

        await new PurchaseContractsPriceFixationsApprovalService(_db.Context, FixedVolume())
            .ExecuteAsync(fixation.Key, null, "diretoria");

        // InApproval já reservava volume — aprovar não muda o volume reservado.
        Assert.Equal(30_000m, (await ReloadContractAsync(contract.Key)).FixedVolume);
    }

    [Fact]
    public async Task Approve_MakesFixationCountTowardTotalPrice()
    {
        var (contract, fixation) = await SeedAsync();

        await new PurchaseContractsPriceFixationsApprovalService(_db.Context, FixedVolume())
            .ExecuteAsync(fixation.Key, null, "diretoria");

        var reloaded = await _db.Context.PurchaseContracts
            .Include(x => x.PriceFixations)
            .AsNoTracking().SingleAsync(x => x.Key == contract.Key);

        // 30.000 * 2,5 = 75.000 — antes da aprovação valia zero.
        Assert.Equal(75_000m, reloaded.TotalPrice);
    }

    [Fact]
    public async Task Approve_AlreadyConfirmed_Throws()
    {
        var (_, fixation) = await SeedAsync(status: PriceFixationStatus.Confirmed);

        await Assert.ThrowsAsync<ApplicationException>(() =>
            new PurchaseContractsPriceFixationsApprovalService(_db.Context, FixedVolume())
                .ExecuteAsync(fixation.Key, null, "diretoria"));
    }

    [Fact]
    public async Task Approve_OnFinishedContract_Throws()
    {
        var (_, fixation) = await SeedAsync(contractStatus: ContractStatus.Finished);

        await Assert.ThrowsAsync<ApplicationException>(() =>
            new PurchaseContractsPriceFixationsApprovalService(_db.Context, FixedVolume())
                .ExecuteAsync(fixation.Key, null, "diretoria"));
    }

    [Fact]
    public async Task Approve_UnknownFixation_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            new PurchaseContractsPriceFixationsApprovalService(_db.Context, FixedVolume())
                .ExecuteAsync(Guid.NewGuid(), null, "diretoria"));
    }

    [Fact]
    public async Task Reject_InApprovalFixation_BecomesRejected_AndReleasesVolume()
    {
        var (contract, fixation) = await SeedAsync();

        await new PurchaseContractsPriceFixationsRejectService(_db.Context, FixedVolume())
            .ExecuteAsync(fixation.Key, "preço fora do mercado", "diretoria");

        var reloadedFixation = await ReloadFixationAsync(fixation.Key);
        Assert.Equal(PriceFixationStatus.Rejected, reloadedFixation.Status);
        Assert.Equal("preço fora do mercado", reloadedFixation.ApprovalComments);

        var reloadedContract = await ReloadContractAsync(contract.Key);
        Assert.Equal(0m, reloadedContract.FixedVolume);
        Assert.Equal(100_000m, reloadedContract.AvailableVolumeToPricing);
    }

    [Fact]
    public async Task Reject_AlreadyConfirmed_Throws()
    {
        var (_, fixation) = await SeedAsync(status: PriceFixationStatus.Confirmed);

        await Assert.ThrowsAsync<ApplicationException>(() =>
            new PurchaseContractsPriceFixationsRejectService(_db.Context, FixedVolume())
                .ExecuteAsync(fixation.Key, null, "diretoria"));
    }
}
