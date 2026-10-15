using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesContracts.Washouts;

/// <summary>Spec 2026-10-15 (washout de venda) §5.2 e §5.3.</summary>
public class SalesContractsWashoutApprovalServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesContractsWashoutApprovalService Approval() => new(
        _db.Context,
        new SalesContractsWashedOutVolumeService(_db.Context),
        new SalesContractsChangeLogService(_db.Context),
        TestNotificationOutbox.For(_db.Context),
        new FinancialDocumentsAdjustProvisionalService(
            _db.Context, FinancialDocumentTestServices.Generate(_db.Context), new FinancialDocumentChangeLogService(_db.Context)),
        new FinancialDocumentsGenerateWashoutReceivableService(
            _db.Context, new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(new Dictionary<string, string> { ["C0001"] = "CLIENTE TESTE" })));

    private SalesContractsWashoutRejectService Reject() => new(
        _db.Context,
        new SalesContractsWashedOutVolumeService(_db.Context),
        new SalesContractsChangeLogService(_db.Context),
        TestNotificationOutbox.For(_db.Context));

    /// <summary>
    /// Preço fixo 1.000 KG; fixação automática 1.000 @ 100 com provisório de 100.000; um washout em aprovação de
    /// <paramref name="fixedVolume"/> a mercado 90 + multa 500. Conta CX01 para a baixa.
    /// </summary>
    private async Task<(SalesContract Contract, SalesContractWashout Washout, FinancialDocument Provisional)> SeedAsync(
        decimal fixedVolume = 100m, decimal marketPrice = 90m, decimal penaltyAmount = 500m, decimal composedAmount = 0m)
    {
        var contract = SalesWashoutTestData.Contract();
        contract.FixedVolume = 1_000m;
        contract.WashedOutVolume = fixedVolume;
        var fixation = SalesWashoutTestData.Fixation(contract, 1_000m);
        var provisional = SalesWashoutTestData.Provisional(contract, fixation);
        provisional.ComposedAmount = composedAmount;
        var washout = SalesWashoutTestData.Washout(contract, fixation, fixedVolume: fixedVolume,
            marketPrice: marketPrice, penaltyAmount: penaltyAmount);

        _db.Context.FinancialAccounts.Add(new FinancialAccount
        {
            Code = "CX01", Name = "Caixa", Type = FinancialAccountType.Cash, Currency = CurrencyType.Brl,
        });
        _db.Context.SalesContracts.Add(contract);
        _db.Context.SalesContractsPriceFixations.Add(fixation);
        _db.Context.FinancialDocuments.Add(provisional);
        _db.Context.SalesContractsWashouts.Add(washout);
        await _db.Context.SaveChangesAsync();
        return (contract, washout, provisional);
    }

    private Task<FinancialDocument> ReloadDocumentAsync(Guid key) =>
        _db.Context.FinancialDocuments.AsNoTracking().SingleAsync(d => d.Key == key);

    private Task<SalesContractWashout> ReloadAsync(Guid key) =>
        _db.Context.SalesContractsWashouts.AsNoTracking().SingleAsync(w => w.Key == key);

    private IQueryable<FinancialDocument> WashoutTitles() =>
        _db.Context.FinancialDocuments.AsNoTracking().Where(d => d.OriginType == FinancialDocumentOrigin.SalesContractWashout);

    [Fact]
    public async Task Approve_marks_approved_and_records_the_approver()
    {
        var (_, washout, _) = await SeedAsync();

        await Approval().ExecuteAsync(washout.Key, "ok", "diretoria");

        var reloaded = await ReloadAsync(washout.Key);
        Assert.Equal(SalesContractWashoutStatus.Approved, reloaded.Status);
        Assert.Equal("diretoria", reloaded.ApprovedBy);
        Assert.Equal("ok", reloaded.ApprovalComments);
        Assert.Contains(_db.Context.NotificationOutboxMessages, m => m.EventType == NotificationEventType.WashoutApproved);
    }

    [Fact]
    public async Task Approve_reduces_the_fixation_provisional_in_place()
    {
        var (_, washout, provisional) = await SeedAsync();

        await Approval().ExecuteAsync(washout.Key, null, "diretoria");

        var reloaded = await ReloadDocumentAsync(provisional.Key);
        Assert.Equal(FinancialDocumentStatus.Open, reloaded.Status);
        Assert.Equal(90_000m, reloaded.NetAmount);
    }

    [Fact]
    public async Task Approve_generates_the_receivable_and_links_it()
    {
        var (contract, washout, _) = await SeedAsync();

        await Approval().ExecuteAsync(washout.Key, null, "diretoria");

        var title = Assert.Single(WashoutTitles());
        Assert.Equal(FinancialDocumentNature.Firm, title.Nature);
        Assert.Equal(FinancialDirection.Receivable, title.Direction);
        Assert.Equal(1_500m, title.NetAmount);
        Assert.Equal(contract.Key, title.SalesContractKey);
        Assert.Equal(title.Key, (await ReloadAsync(washout.Key)).FinancialDocumentKey);
    }

    [Fact]
    public async Task Approve_without_amount_generates_no_receivable()
    {
        var (_, washout, _) = await SeedAsync(marketPrice: 120m, penaltyAmount: 0m);

        await Approval().ExecuteAsync(washout.Key, null, "diretoria");

        Assert.Empty(WashoutTitles());
        Assert.Null((await ReloadAsync(washout.Key)).FinancialDocumentKey);
    }

    [Fact]
    public async Task Approve_washing_the_whole_fixation_cancels_the_provisional()
    {
        var (_, washout, provisional) = await SeedAsync(fixedVolume: 1_000m);

        await Approval().ExecuteAsync(washout.Key, null, "diretoria");

        var reloaded = await ReloadDocumentAsync(provisional.Key);
        Assert.Equal(FinancialDocumentStatus.Canceled, reloaded.Status);
        Assert.Equal("Volume lavado por washout", reloaded.CancellationReason);
    }

    [Fact]
    public async Task Approve_below_the_composed_amount_is_refused_and_nothing_changes()
    {
        // Provisório de 100.000 com 95.000 compostos; o washout de 100 o levaria a 90.000.
        var (_, washout, provisional) = await SeedAsync(composedAmount: 95_000m);

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            Approval().ExecuteAsync(washout.Key, null, "diretoria"));

        Assert.Equal(
            "O provisório FN-PROV-V1 já tem 95.000,00 composto: o washout o deixaria em 90.000,00. Desfaça a composição antes de aprovar.",
            error.Message);
        Assert.Equal(SalesContractWashoutStatus.InApproval, (await ReloadAsync(washout.Key)).Status);
        Assert.Equal(100_000m, (await ReloadDocumentAsync(provisional.Key)).NetAmount);
        Assert.Empty(WashoutTitles());
    }

    [Fact]
    public async Task Washout_receivable_settles_directly_and_is_not_composable()
    {
        var (_, washout, provisional) = await SeedAsync();
        await Approval().ExecuteAsync(washout.Key, null, "diretoria");
        var title = Assert.Single(WashoutTitles());

        var composeError = await Assert.ThrowsAsync<ApplicationException>(() =>
            new FinancialDocumentsComposeService(_db, new FinancialDocumentChangeLogService(_db.Context))
                .ExecuteAsync(provisional.Key, [title.Key], "ana"));
        Assert.Equal($"O título {title.Code} não é firme nem devolução de documento de saída.", composeError.Message);

        await new FinancialDocumentsSettleService(_db)
            .ExecuteAsync(title.Key, "CX01", 1_500m, DateTime.Today, 0m, 0m, 0m, null, null, "ana");

        Assert.Equal(FinancialDocumentStatus.Settled, (await ReloadDocumentAsync(title.Key)).Status);
    }

    [Fact]
    public async Task Approve_revalidates_against_the_current_state()
    {
        var (contract, washout, _) = await SeedAsync();
        _db.Context.SalesShipmentReleases.Add(SalesContractsAllocationTestSupport.NewRelease(contract.Key, released: 950m));
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Approval().ExecuteAsync(washout.Key, null, "diretoria"));

        Assert.Contains("excede o saldo não liberado", error.Message);
    }

    [Fact]
    public async Task Approving_twice_is_refused()
    {
        var (_, washout, _) = await SeedAsync();
        await Approval().ExecuteAsync(washout.Key, null, "diretoria");

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Approval().ExecuteAsync(washout.Key, null, "diretoria"));

        Assert.Equal("Só é possível aprovar washout em aprovação. Status atual: Approved.", error.Message);
    }

    [Fact]
    public async Task Reject_returns_the_volume_and_requires_a_reason()
    {
        var (contract, washout, provisional) = await SeedAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Reject().ExecuteAsync(washout.Key, " ", "diretoria"));
        Assert.Equal("Informe o motivo da rejeição.", error.Message);

        await Reject().ExecuteAsync(washout.Key, "sem respaldo", "diretoria");

        var reloaded = await ReloadAsync(washout.Key);
        Assert.Equal(SalesContractWashoutStatus.Rejected, reloaded.Status);
        Assert.Equal("sem respaldo", reloaded.ApprovalComments);
        Assert.Equal(0m, (await _db.Context.SalesContracts.AsNoTracking().SingleAsync(c => c.Key == contract.Key)).WashedOutVolume);
        Assert.Equal(100_000m, (await ReloadDocumentAsync(provisional.Key)).NetAmount);
        Assert.Contains(_db.Context.NotificationOutboxMessages, m => m.EventType == NotificationEventType.WashoutRejected);
    }
}
