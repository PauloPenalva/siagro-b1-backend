using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesContracts.Washouts;

/// <summary>Spec 2026-10-15 (washout de venda) §5.3/5.4.</summary>
public class SalesContractsWashoutReverseServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private FinancialDocumentsAdjustProvisionalService Adjust() => new(
        _db.Context, FinancialDocumentTestServices.Generate(_db.Context), new FinancialDocumentChangeLogService(_db.Context));

    private SalesContractsWashoutApprovalService Approval() => new(
        _db.Context, new SalesContractsWashedOutVolumeService(_db.Context), new SalesContractsChangeLogService(_db.Context),
        TestNotificationOutbox.For(_db.Context), Adjust(),
        new FinancialDocumentsGenerateWashoutReceivableService(
            _db.Context, new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(new Dictionary<string, string> { ["C0001"] = "CLIENTE TESTE" })));

    private SalesContractsWashoutReverseService Reverse() => new(
        _db.Context, new SalesContractsWashedOutVolumeService(_db.Context), new SalesContractsChangeLogService(_db.Context),
        TestNotificationOutbox.For(_db.Context), Adjust(), FinancialDocumentTestServices.Cancel(_db.Context));

    /// <summary>Washout de <paramref name="fixedVolume"/> registrado e APROVADO pelo serviço real.</summary>
    private async Task<(SalesContract Contract, SalesContractWashout Washout, FinancialDocument Provisional)> ApprovedAsync(
        decimal fixedVolume = 100m)
    {
        var contract = SalesWashoutTestData.Contract();
        contract.FixedVolume = 1_000m;
        contract.WashedOutVolume = fixedVolume;
        var fixation = SalesWashoutTestData.Fixation(contract, 1_000m);
        var provisional = SalesWashoutTestData.Provisional(contract, fixation);
        var washout = SalesWashoutTestData.Washout(contract, fixation, fixedVolume: fixedVolume);
        _db.Context.SalesContracts.Add(contract);
        _db.Context.SalesContractsPriceFixations.Add(fixation);
        _db.Context.FinancialDocuments.Add(provisional);
        _db.Context.SalesContractsWashouts.Add(washout);
        await _db.Context.SaveChangesAsync();

        await Approval().ExecuteAsync(washout.Key, null, "diretoria");
        return (contract, washout, provisional);
    }

    private Task<FinancialDocument> ReloadDocumentAsync(Guid key) =>
        _db.Context.FinancialDocuments.AsNoTracking().SingleAsync(d => d.Key == key);

    [Fact]
    public async Task Reverse_cancels_the_title_restores_the_provisional_and_returns_the_volume()
    {
        var (contract, washout, provisional) = await ApprovedAsync();
        var titleKey = (await _db.Context.SalesContractsWashouts.AsNoTracking().SingleAsync(w => w.Key == washout.Key)).FinancialDocumentKey!.Value;

        await Reverse().ExecuteAsync(washout.Key, "lançado errado", "ana");

        var reloaded = await _db.Context.SalesContractsWashouts.AsNoTracking().SingleAsync(w => w.Key == washout.Key);
        Assert.Equal(SalesContractWashoutStatus.Reversed, reloaded.Status);
        Assert.Equal("lançado errado", reloaded.ReversalReason);

        var title = await ReloadDocumentAsync(titleKey);
        Assert.Equal(FinancialDocumentStatus.Canceled, title.Status);
        Assert.Equal("Washout estornado", title.CancellationReason);

        Assert.Equal(100_000m, (await ReloadDocumentAsync(provisional.Key)).NetAmount);
        Assert.Equal(0m, (await _db.Context.SalesContracts.AsNoTracking().SingleAsync(c => c.Key == contract.Key)).WashedOutVolume);
        Assert.Contains(_db.Context.NotificationOutboxMessages, m => m.EventType == NotificationEventType.WashoutReversed);
    }

    [Fact]
    public async Task Reverse_after_a_full_washout_generates_a_new_provisional()
    {
        var (_, washout, provisional) = await ApprovedAsync(fixedVolume: 1_000m);
        Assert.Equal(FinancialDocumentStatus.Canceled, (await ReloadDocumentAsync(provisional.Key)).Status);

        await Reverse().ExecuteAsync(washout.Key, "lançado errado", "ana");

        var active = await _db.Context.FinancialDocuments.AsNoTracking()
            .Where(d => d.OriginType == FinancialDocumentOrigin.SalesContractPriceFixation && d.Status != FinancialDocumentStatus.Canceled)
            .ToListAsync();
        Assert.Equal(100_000m, Assert.Single(active).NetAmount);
    }

    [Fact]
    public async Task Reverse_is_refused_when_the_title_has_a_settlement()
    {
        var (_, washout, _) = await ApprovedAsync();
        var title = await _db.Context.FinancialDocuments.SingleAsync(d => d.OriginType == FinancialDocumentOrigin.SalesContractWashout);
        title.SettledAmount = 100m;
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Reverse().ExecuteAsync(washout.Key, "x", "ana"));

        Assert.Equal($"O documento {title.Code} possui baixas. Estorne a baixa antes.", error.Message);
        Assert.Equal(SalesContractWashoutStatus.Approved,
            (await _db.Context.SalesContractsWashouts.AsNoTracking().SingleAsync(w => w.Key == washout.Key)).Status);
    }

    [Fact]
    public async Task Reverse_on_a_finished_contract_asks_to_reopen()
    {
        var (contract, washout, _) = await ApprovedAsync();
        var tracked = await _db.Context.SalesContracts.SingleAsync(c => c.Key == contract.Key);
        tracked.Status = ContractStatus.Finished;
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Reverse().ExecuteAsync(washout.Key, "x", "ana"));

        Assert.Equal("Contrato precisa estar aprovado para estornar washout. Reabra o contrato antes.", error.Message);
    }

    [Fact]
    public async Task Only_approved_washouts_can_be_reversed()
    {
        var contract = SalesWashoutTestData.Contract(ContractType.ToBeDetermined);
        var washout = SalesWashoutTestData.Washout(contract, unfixedVolume: 10m);
        _db.Context.SalesContracts.Add(contract);
        _db.Context.SalesContractsWashouts.Add(washout);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Reverse().ExecuteAsync(washout.Key, "x", "ana"));

        Assert.Equal("Só é possível estornar washout aprovado. Status atual: InApproval.", error.Message);
    }

    [Fact]
    public async Task Reverse_requires_a_reason()
    {
        var (_, washout, _) = await ApprovedAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Reverse().ExecuteAsync(washout.Key, null, "ana"));

        Assert.Equal("Informe o motivo do estorno.", error.Message);
    }
}
