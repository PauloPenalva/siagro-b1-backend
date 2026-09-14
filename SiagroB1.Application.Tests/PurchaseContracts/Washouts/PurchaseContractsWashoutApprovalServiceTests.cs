using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts.Washouts;

public class PurchaseContractsWashoutApprovalServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseContractsWashoutApprovalService Approval() => new(
        _db.Context,
        new PurchaseContractsWashedOutVolumeService(_db.Context),
        new PurchaseContractsChangeLogService(_db.Context),
        TestNotificationOutbox.For(_db.Context),
        new FinancialDocumentsAdjustProvisionalService(
            _db.Context, FinancialDocumentTestServices.Generate(_db.Context), new FinancialDocumentChangeLogService(_db.Context)),
        new FinancialDocumentsGenerateWashoutReceivableService(
            _db.Context, new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(new Dictionary<string, string> { ["F0001"] = "PRODUTOR TESTE" })));

    private PurchaseContractsWashoutRejectService Reject() => new(
        _db.Context,
        new PurchaseContractsWashedOutVolumeService(_db.Context),
        new PurchaseContractsChangeLogService(_db.Context),
        TestNotificationOutbox.For(_db.Context));

    /// <summary>
    /// Preço fixo 100.000 kg; fixação automática 100.000 @ 2,50 com provisório de 250.000; um
    /// washout em aprovação de <paramref name="fixedVolume"/> a mercado 2,75 + multa 1.000.
    /// </summary>
    private async Task<(PurchaseContract Contract, PurchaseContractWashout Washout)> SeedFixAsync(
        decimal fixedVolume = 10_000m, decimal marketPrice = 2.75m, decimal penaltyAmount = 1_000m)
    {
        var contract = WashoutTestData.Contract();
        contract.FixedVolume = 100_000m;
        contract.WashedOutVolume = fixedVolume;
        var fixation = WashoutTestData.Fixation(contract, 100_000m);
        var washout = WashoutTestData.Washout(contract, fixation, fixedVolume: fixedVolume,
            marketPrice: marketPrice, penaltyAmount: penaltyAmount);

        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsPriceFixations.Add(fixation);
        _db.Context.FinancialDocuments.Add(WashoutTestData.Provisional(contract, fixation));
        _db.Context.PurchaseContractsWashouts.Add(washout);
        await _db.Context.SaveChangesAsync();
        return (contract, washout);
    }

    private IQueryable<FinancialDocument> Payables() =>
        _db.Context.FinancialDocuments.AsNoTracking().Where(x => x.Direction == FinancialDirection.Payable);

    private IQueryable<FinancialDocument> Receivables() =>
        _db.Context.FinancialDocuments.AsNoTracking().Where(x => x.Direction == FinancialDirection.Receivable);

    private async Task<PurchaseContractWashout> ReloadAsync(Guid key) =>
        await _db.Context.PurchaseContractsWashouts.AsNoTracking().SingleAsync(x => x.Key == key);

    [Fact]
    public async Task Approve_marks_approved_and_records_the_approver()
    {
        var (_, washout) = await SeedFixAsync();

        await Approval().ExecuteAsync(washout.Key, "ok", "diretoria");

        var reloaded = await ReloadAsync(washout.Key);
        Assert.Equal(PurchaseContractWashoutStatus.Approved, reloaded.Status);
        Assert.Equal("diretoria", reloaded.ApprovedBy);
        Assert.Equal("ok", reloaded.ApprovalComments);
        Assert.NotNull(reloaded.ApprovedAt);
        Assert.Contains(_db.Context.NotificationOutboxMessages, m => m.EventType == NotificationEventType.WashoutApproved);
    }

    [Fact]
    public async Task Approve_reduces_the_fixation_provisional_in_place()
    {
        var (_, washout) = await SeedFixAsync();

        await Approval().ExecuteAsync(washout.Key, null, "diretoria");

        var provisional = Assert.Single(Payables());
        Assert.Equal(FinancialDocumentStatus.Open, provisional.Status);
        Assert.Equal(225_000m, provisional.NetAmount);
        Assert.Equal(FinancialDocumentChangeLogFields.NetAmount, _db.Context.FinancialDocumentChangeLogs.Single().Field);
    }

    [Fact]
    public async Task Approve_generates_the_receivable_and_links_it()
    {
        var (contract, washout) = await SeedFixAsync();

        await Approval().ExecuteAsync(washout.Key, null, "diretoria");

        var receivable = Assert.Single(Receivables());
        Assert.Equal(FinancialDocumentNature.Firm, receivable.Nature);
        Assert.Equal(3_500m, receivable.NetAmount);
        Assert.Equal(new DateTime(2026, 10, 31), receivable.DueDate);
        Assert.Equal(FinancialDocumentOrigin.PurchaseContractWashout, receivable.OriginType);
        Assert.Equal(washout.Key, receivable.OriginKey);
        Assert.Equal(contract.Key, receivable.PurchaseContractKey);
        Assert.Equal(receivable.Key, (await ReloadAsync(washout.Key)).FinancialDocumentKey);
    }

    [Fact]
    public async Task Approve_without_amount_generates_no_receivable()
    {
        var (_, washout) = await SeedFixAsync(marketPrice: 2.0m, penaltyAmount: 0m);

        await Approval().ExecuteAsync(washout.Key, null, "diretoria");

        Assert.Empty(Receivables());
        Assert.Null((await ReloadAsync(washout.Key)).FinancialDocumentKey);
    }

    [Fact]
    public async Task Approve_washing_the_whole_fixation_cancels_the_provisional()
    {
        var (_, washout) = await SeedFixAsync(fixedVolume: 100_000m);

        await Approval().ExecuteAsync(washout.Key, null, "diretoria");

        Assert.Equal(FinancialDocumentStatus.Canceled, Assert.Single(Payables()).Status);
    }

    [Fact]
    public async Task Approve_unfixed_only_washout_does_not_touch_the_provisional()
    {
        var contract = WashoutTestData.Contract(ContractType.ToBeDetermined);
        contract.FixedVolume = 30_000m;
        contract.WashedOutVolume = 20_000m;
        contract.WashedOutUnfixedVolume = 20_000m;
        var fixation = WashoutTestData.Fixation(contract, 30_000m);
        var washout = WashoutTestData.Washout(contract, unfixedVolume: 20_000m);
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsPriceFixations.Add(fixation);
        _db.Context.FinancialDocuments.Add(WashoutTestData.Provisional(contract, fixation));
        _db.Context.PurchaseContractsWashouts.Add(washout);
        await _db.Context.SaveChangesAsync();

        await Approval().ExecuteAsync(washout.Key, null, "diretoria");

        Assert.Equal(75_000m, Assert.Single(Payables()).NetAmount);
    }

    [Fact]
    public async Task Approve_revalidates_the_balance_with_the_current_contract()
    {
        var (contract, washout) = await SeedFixAsync();
        var tracked = await _db.Context.PurchaseContracts.SingleAsync(x => x.Key == contract.Key);
        tracked.AllocatedVolume = 95_000m;
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Approval().ExecuteAsync(washout.Key, null, "diretoria"));

        Assert.Contains("saldo físico", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Approve_refuses_a_washout_that_is_not_in_approval()
    {
        var (_, washout) = await SeedFixAsync();
        await Approval().ExecuteAsync(washout.Key, null, "diretoria");

        await Assert.ThrowsAsync<ApplicationException>(() => Approval().ExecuteAsync(washout.Key, null, "diretoria"));
    }

    [Fact]
    public async Task Approve_refuses_a_comment_longer_than_500_characters()
    {
        var (_, washout) = await SeedFixAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Approval().ExecuteAsync(washout.Key, new string('a', 501), "diretoria"));

        Assert.Contains("500 caracteres", error.Message);
    }

    [Fact]
    public async Task Approve_trims_the_comments()
    {
        var (_, washout) = await SeedFixAsync();

        await Approval().ExecuteAsync(washout.Key, "  ok  ", "diretoria");

        Assert.Equal("ok", (await ReloadAsync(washout.Key)).ApprovalComments);
    }

    [Fact]
    public async Task Reject_requires_a_reason()
    {
        var (_, washout) = await SeedFixAsync();

        await Assert.ThrowsAsync<ApplicationException>(() => Reject().ExecuteAsync(washout.Key, "  ", "diretoria"));
    }

    [Fact]
    public async Task Reject_refuses_a_reason_longer_than_500_characters()
    {
        var (_, washout) = await SeedFixAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Reject().ExecuteAsync(washout.Key, new string('a', 501), "diretoria"));

        Assert.Contains("500 caracteres", error.Message);
    }

    [Fact]
    public async Task Reject_marks_rejected_and_releases_the_volume()
    {
        var (contract, washout) = await SeedFixAsync();

        await Reject().ExecuteAsync(washout.Key, "mercado errado", "diretoria");

        var reloaded = await ReloadAsync(washout.Key);
        Assert.Equal(PurchaseContractWashoutStatus.Rejected, reloaded.Status);
        Assert.Equal("mercado errado", reloaded.ApprovalComments);
        Assert.Equal("diretoria", reloaded.CanceledBy);
        Assert.Equal(0m, (await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == contract.Key)).WashedOutVolume);
        Assert.Contains(_db.Context.NotificationOutboxMessages, m => m.EventType == NotificationEventType.WashoutRejected);
        Assert.Equal(250_000m, Assert.Single(Payables()).NetAmount);
    }
}
