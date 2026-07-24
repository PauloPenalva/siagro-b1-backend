using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

/// <summary>
/// O ciclo de vida da fixação de preço no log de alterações do contrato de compra: criação,
/// aprovação, rejeição, estorno e exclusão. Espelho de
/// <c>SalesContractsPriceFixationChangeLogTests</c>.
/// </summary>
public class PurchaseContractsPriceFixationChangeLogTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseContractsChangeLogService ChangeLog() => new(_db.Context);

    private PurchaseContractsFixedVolumeService FixedVolume() => new(_db.Context);

    private PurchaseContractsPriceFixationCreateService CreateService() => new(
        _db.Context, FixedVolume(), ChangeLog(),
        NullLogger<PurchaseContractsPriceFixationCreateService>.Instance);

    private PurchaseContractsPriceFixationsApprovalService ApprovalService() =>
        new(_db.Context, FixedVolume(), ChangeLog());

    private PurchaseContractsPriceFixationsRejectService RejectService() =>
        new(_db.Context, FixedVolume(), ChangeLog());

    private PurchaseContractsPriceFixationsCancelService CancelService() =>
        new(_db.Context, FixedVolume(), ChangeLog());

    private PurchaseContractsPriceFixationDeleteService DeleteService() => new(
        _db.Context, FixedVolume(), ChangeLog(),
        NullLogger<PurchaseContractsPriceFixationDeleteService>.Instance);

    private async Task<PurchaseContract> SeedContractAsync()
    {
        var pc = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC-001", CardCode = "F0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25",
            DeliveryLocationCode = "01",
            TotalVolume = 1_000_000m,
            Status = ContractStatus.Approved,
            Type = ContractType.ToBeDetermined,
        };
        _db.Context.PurchaseContracts.Add(pc);
        await _db.Context.SaveChangesAsync();
        return pc;
    }

    private async Task<PurchaseContractPriceFixation> CreateFixationAsync(
        PurchaseContract contract, decimal volume = 10_000m, decimal price = 2.5m)
    {
        return await CreateService().ExecuteAsync(
            contract.Key,
            new PurchaseContractPriceFixation { FixationVolume = volume, FixationPrice = price },
            "joao");
    }

    private List<PurchaseContractChangeLog> FixationLogsOf(Guid contractKey) =>
        _db.Context.PurchaseContractsChangeLogs
            .Where(l => l.PurchaseContractKey == contractKey
                        && l.Field == ContractChangeLogFields.PriceFixation)
            .ToList();

    [Fact]
    public async Task Create_IsLoggedAsInclusionInApproval()
    {
        var pc = await SeedContractAsync();

        await CreateFixationAsync(pc);

        var log = Assert.Single(FixationLogsOf(pc.Key));
        Assert.Null(log.OldValue);
        Assert.Contains("10.000,000", log.NewValue);
        Assert.Contains("Em aprovação", log.NewValue);
        Assert.Equal("joao", log.ChangedBy);
    }

    [Fact]
    public async Task Approval_IsLoggedAsTransitionToConfirmed()
    {
        var pc = await SeedContractAsync();
        var fixation = await CreateFixationAsync(pc);

        await ApprovalService().ExecuteAsync(fixation.Key, "ok", "diretor");

        var log = Assert.Single(FixationLogsOf(pc.Key).Where(l => l.ChangedBy == "diretor"));
        Assert.Contains("Em aprovação", log.OldValue);
        Assert.Contains("Confirmada", log.NewValue);
        // O volume identifica QUAL fixação mudou.
        Assert.Contains("10.000,000", log.NewValue);
    }

    [Fact]
    public async Task Reject_IsLoggedAsTransitionToRejected()
    {
        var pc = await SeedContractAsync();
        var fixation = await CreateFixationAsync(pc);

        await RejectService().ExecuteAsync(fixation.Key, "preço fora do mercado", "diretor");

        var log = Assert.Single(FixationLogsOf(pc.Key).Where(l => l.ChangedBy == "diretor"));
        Assert.Contains("Em aprovação", log.OldValue);
        Assert.Contains("Rejeitada", log.NewValue);
    }

    [Fact]
    public async Task Cancel_IsLoggedAsTransitionBackToInApproval()
    {
        var pc = await SeedContractAsync();
        var fixation = await CreateFixationAsync(pc);
        await ApprovalService().ExecuteAsync(fixation.Key, "ok", "diretor");

        await CancelService().ExecuteAsync(fixation.Key, "maria");

        var log = Assert.Single(FixationLogsOf(pc.Key).Where(l => l.ChangedBy == "maria"));
        Assert.Contains("Confirmada", log.OldValue);
        Assert.Contains("Em aprovação", log.NewValue);
    }

    [Fact]
    public async Task Delete_IsLoggedAsRemoval()
    {
        var pc = await SeedContractAsync();
        var fixation = await CreateFixationAsync(pc);

        await DeleteService().ExecuteAsync(fixation.Key, "maria");

        var log = Assert.Single(FixationLogsOf(pc.Key).Where(l => l.ChangedBy == "maria"));
        Assert.Contains("10.000,000", log.OldValue);
        Assert.Null(log.NewValue);
    }

    [Fact]
    public async Task FullLifecycle_ProducesOneLineEach()
    {
        var pc = await SeedContractAsync();
        var fixation = await CreateFixationAsync(pc);
        await ApprovalService().ExecuteAsync(fixation.Key, "ok", "diretor");
        await CancelService().ExecuteAsync(fixation.Key, "maria");
        await DeleteService().ExecuteAsync(fixation.Key, "maria");

        Assert.Equal(4, FixationLogsOf(pc.Key).Count);
    }
}
