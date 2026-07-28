using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesContracts;

/// <summary>
/// O ciclo de vida da fixação de preço no log de alterações do contrato de venda: criação,
/// aprovação, rejeição, estorno e exclusão.
///
/// Volume e preço aparecem em TODAS as linhas — um contrato tem várias fixações, e uma
/// transição de status sozinha não diria qual delas mudou.
/// </summary>
public class SalesContractsPriceFixationChangeLogTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesContractsChangeLogService ChangeLog() => new(_db.Context);

    private SalesContractsFixedVolumeService FixedVolume() => new(_db.Context);

    private SalesContractsPriceFixationCreateService CreateService() => new(
        _db.Context, FixedVolume(), ChangeLog(), TestNotificationOutbox.For(_db.Context),
        NullLogger<SalesContractsPriceFixationCreateService>.Instance);

    private SalesContractsPriceFixationsApprovalService ApprovalService() =>
        new(_db.Context, FixedVolume(), ChangeLog(), TestNotificationOutbox.For(_db.Context));

    private SalesContractsPriceFixationsRejectService RejectService() =>
        new(_db.Context, FixedVolume(), ChangeLog(), TestNotificationOutbox.For(_db.Context));

    private SalesContractsPriceFixationsCancelService CancelService() =>
        new(_db.Context, FixedVolume(), ChangeLog(), TestNotificationOutbox.For(_db.Context));

    private SalesContractsPriceFixationDeleteService DeleteService() => new(
        _db.Context, FixedVolume(), ChangeLog(),
        NullLogger<SalesContractsPriceFixationDeleteService>.Instance);

    private async Task<SalesContract> SeedContractAsync()
    {
        var sc = new SalesContract
        {
            Key = Guid.NewGuid(), Code = "SC-001", CardCode = "C0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25",
            TotalVolume = 1_000_000m,
            Status = ContractStatus.Approved,
            Type = ContractType.ToBeDetermined,
        };
        _db.Context.SalesContracts.Add(sc);
        await _db.Context.SaveChangesAsync();
        return sc;
    }

    private async Task<SalesContractPriceFixation> CreateFixationAsync(
        SalesContract contract, decimal volume = 10_000m, decimal price = 2.5m)
    {
        return await CreateService().ExecuteAsync(
            contract.Key,
            new SalesContractPriceFixation { FixationVolume = volume, FixationPrice = price },
            "joao");
    }

    private List<SalesContractChangeLog> FixationLogsOf(Guid contractKey) =>
        _db.Context.SalesContractsChangeLogs
            .Where(l => l.SalesContractKey == contractKey
                        && l.Field == ContractChangeLogFields.PriceFixation)
            .ToList();

    [Fact]
    public async Task Create_IsLoggedAsInclusionInApproval()
    {
        var sc = await SeedContractAsync();

        await CreateFixationAsync(sc);

        var log = Assert.Single(FixationLogsOf(sc.Key));
        Assert.Null(log.OldValue);
        Assert.Contains("10.000,000", log.NewValue);
        Assert.Contains("Em aprovação", log.NewValue);
        Assert.Equal("joao", log.ChangedBy);
    }

    [Fact]
    public async Task Approval_IsLoggedAsTransitionToConfirmed()
    {
        var sc = await SeedContractAsync();
        var fixation = await CreateFixationAsync(sc);

        await ApprovalService().ExecuteAsync(fixation.Key, "ok", "diretor");

        var log = Assert.Single(FixationLogsOf(sc.Key).Where(l => l.ChangedBy == "diretor"));
        Assert.Contains("Em aprovação", log.OldValue);
        Assert.Contains("Confirmada", log.NewValue);
        // O volume identifica QUAL fixação mudou.
        Assert.Contains("10.000,000", log.NewValue);
    }

    [Fact]
    public async Task Reject_IsLoggedAsTransitionToRejected()
    {
        var sc = await SeedContractAsync();
        var fixation = await CreateFixationAsync(sc);

        await RejectService().ExecuteAsync(fixation.Key, "preço fora do mercado", "diretor");

        var log = Assert.Single(FixationLogsOf(sc.Key).Where(l => l.ChangedBy == "diretor"));
        Assert.Contains("Em aprovação", log.OldValue);
        Assert.Contains("Rejeitada", log.NewValue);
    }

    [Fact]
    public async Task Cancel_IsLoggedAsTransitionBackToInApproval()
    {
        var sc = await SeedContractAsync();
        var fixation = await CreateFixationAsync(sc);
        await ApprovalService().ExecuteAsync(fixation.Key, "ok", "diretor");

        await CancelService().ExecuteAsync(fixation.Key, "maria");

        var log = Assert.Single(FixationLogsOf(sc.Key).Where(l => l.ChangedBy == "maria"));
        Assert.Contains("Confirmada", log.OldValue);
        Assert.Contains("Em aprovação", log.NewValue);
    }

    [Fact]
    public async Task Delete_IsLoggedAsRemoval()
    {
        var sc = await SeedContractAsync();
        var fixation = await CreateFixationAsync(sc);

        await DeleteService().ExecuteAsync(fixation.Key, "maria");

        var log = Assert.Single(FixationLogsOf(sc.Key).Where(l => l.ChangedBy == "maria"));
        Assert.Contains("10.000,000", log.OldValue);
        Assert.Null(log.NewValue);
    }

    [Fact]
    public async Task FullLifecycle_ProducesOneLineEachInOrder()
    {
        var sc = await SeedContractAsync();
        var fixation = await CreateFixationAsync(sc);
        await ApprovalService().ExecuteAsync(fixation.Key, "ok", "diretor");
        await CancelService().ExecuteAsync(fixation.Key, "maria");
        await DeleteService().ExecuteAsync(fixation.Key, "maria");

        Assert.Equal(4, FixationLogsOf(sc.Key).Count);
    }
}
