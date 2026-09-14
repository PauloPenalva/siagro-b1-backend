using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.PurchaseContracts.Washouts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

public class FinancialDocumentsWashoutTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private FinancialDocumentsGenerateService Generate() => FinancialDocumentTestServices.Generate(_db.Context);

    private FinancialDocumentsAdjustProvisionalService Adjust() =>
        new(_db.Context, Generate(), new FinancialDocumentChangeLogService(_db.Context));

    private FinancialDocumentsGenerateWashoutReceivableService Receivable() => new(
        _db.Context, new FakeDocNumberSequenceService(),
        new FakeBusinessPartnerService(new Dictionary<string, string> { ["F0001"] = "PRODUTOR TESTE" }));

    private async Task<(PurchaseContract Contract, PurchaseContractPriceFixation Fixation)> SeedFixationAsync(
        params (decimal FixedVolume, PurchaseContractWashoutStatus Status)[] washouts)
    {
        var contract = WashoutTestData.Contract();
        var fixation = WashoutTestData.Fixation(contract, 100_000m);
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsPriceFixations.Add(fixation);

        var sequence = 1;
        foreach (var (fixedVolume, status) in washouts)
            _db.Context.PurchaseContractsWashouts.Add(
                WashoutTestData.Washout(contract, fixation, fixedVolume: fixedVolume, status: status, sequence: sequence++));

        await _db.Context.SaveChangesAsync();
        return (contract, fixation);
    }

    [Fact]
    public async Task Generator_discounts_the_approved_washouts_of_the_fixation()
    {
        var (contract, fixation) = await SeedFixationAsync((40_000m, PurchaseContractWashoutStatus.Approved));

        await Generate().EnqueueForPurchaseFixationAsync(contract, fixation, "tester");
        await _db.Context.SaveChangesAsync();

        // (100.000 − 40.000) × 2,5
        Assert.Equal(150_000m, _db.Context.FinancialDocuments.Single().NetAmount);
    }

    [Fact]
    public async Task Generator_ignores_washouts_that_are_not_approved()
    {
        var (contract, fixation) = await SeedFixationAsync(
            (40_000m, PurchaseContractWashoutStatus.InApproval),
            (10_000m, PurchaseContractWashoutStatus.Reversed));

        await Generate().EnqueueForPurchaseFixationAsync(contract, fixation, "tester");
        await _db.Context.SaveChangesAsync();

        Assert.Equal(250_000m, _db.Context.FinancialDocuments.Single().NetAmount);
    }

    [Fact]
    public async Task Generator_skips_a_fully_washed_fixation()
    {
        var (contract, fixation) = await SeedFixationAsync((100_000m, PurchaseContractWashoutStatus.Approved));

        await Generate().EnqueueForPurchaseFixationAsync(contract, fixation, "tester");
        await _db.Context.SaveChangesAsync();

        Assert.Empty(_db.Context.FinancialDocuments);
    }

    [Fact]
    public async Task Generator_uses_the_explicit_remaining_volume()
    {
        var (contract, fixation) = await SeedFixationAsync();

        await Generate().EnqueueForPurchaseFixationAsync(contract, fixation, "tester", remainingVolume: 10_000m);
        await _db.Context.SaveChangesAsync();

        Assert.Equal(25_000m, _db.Context.FinancialDocuments.Single().NetAmount);
    }

    [Fact]
    public async Task Adjust_reduces_the_open_provisional_in_place_and_logs_the_change()
    {
        var (contract, fixation) = await SeedFixationAsync();
        var provisional = WashoutTestData.Provisional(contract, fixation);
        _db.Context.FinancialDocuments.Add(provisional);
        await _db.Context.SaveChangesAsync();

        await Adjust().EnqueueForPurchaseFixationAsync(contract, fixation, 60_000m, "tester");
        await _db.Context.SaveChangesAsync();

        var document = Assert.Single(_db.Context.FinancialDocuments);
        Assert.Equal(provisional.Key, document.Key);
        Assert.Equal(150_000m, document.NetAmount);

        var log = Assert.Single(_db.Context.FinancialDocumentChangeLogs);
        Assert.Equal(FinancialDocumentChangeLogFields.NetAmount, log.Field);
        Assert.Equal("250.000,00", log.OldValue);
        Assert.Equal("150.000,00", log.NewValue);
    }

    [Fact]
    public async Task Adjust_with_an_unchanged_amount_logs_nothing()
    {
        var (contract, fixation) = await SeedFixationAsync();
        _db.Context.FinancialDocuments.Add(WashoutTestData.Provisional(contract, fixation));
        await _db.Context.SaveChangesAsync();

        await Adjust().EnqueueForPurchaseFixationAsync(contract, fixation, 100_000m, "tester");
        await _db.Context.SaveChangesAsync();

        Assert.Empty(_db.Context.FinancialDocumentChangeLogs);
    }

    [Fact]
    public async Task Adjust_cancels_the_provisional_when_nothing_remains()
    {
        var (contract, fixation) = await SeedFixationAsync();
        _db.Context.FinancialDocuments.Add(WashoutTestData.Provisional(contract, fixation));
        await _db.Context.SaveChangesAsync();

        await Adjust().EnqueueForPurchaseFixationAsync(contract, fixation, 0m, "tester");
        await _db.Context.SaveChangesAsync();

        var document = Assert.Single(_db.Context.FinancialDocuments);
        Assert.Equal(FinancialDocumentStatus.Canceled, document.Status);
        Assert.Equal(FinancialDocumentsAdjustProvisionalService.WashedOutCancellationReason, document.CancellationReason);
    }

    [Fact]
    public async Task Adjust_generates_a_provisional_when_none_is_open()
    {
        var (contract, fixation) = await SeedFixationAsync();
        var canceled = WashoutTestData.Provisional(contract, fixation);
        canceled.Status = FinancialDocumentStatus.Canceled;
        _db.Context.FinancialDocuments.Add(canceled);
        await _db.Context.SaveChangesAsync();

        await Adjust().EnqueueForPurchaseFixationAsync(contract, fixation, 30_000m, "tester");
        await _db.Context.SaveChangesAsync();

        Assert.Equal(2, _db.Context.FinancialDocuments.Count());
        Assert.Equal(75_000m, _db.Context.FinancialDocuments.Single(x => x.Status == FinancialDocumentStatus.Open).NetAmount);
    }

    [Fact]
    public async Task Receivable_is_a_firm_open_document_pointing_to_the_washout()
    {
        var (contract, fixation) = await SeedFixationAsync();
        var washout = WashoutTestData.Washout(contract, fixation, fixedVolume: 10_000m);

        var document = await Receivable().EnqueueAsync(contract, washout, "tester");
        await _db.Context.SaveChangesAsync();

        Assert.Equal(FinancialDirection.Receivable, document.Direction);
        Assert.Equal(FinancialDocumentNature.Firm, document.Nature);
        Assert.Equal(FinancialDocumentStatus.Open, document.Status);
        Assert.Equal(3_500m, document.NetAmount);
        Assert.Equal(new DateTime(2026, 10, 31), document.DueDate);
        Assert.Equal(FinancialDocumentOrigin.PurchaseContractWashout, document.OriginType);
        Assert.Equal(washout.Key, document.OriginKey);
        Assert.Equal("PC-001", document.OriginDocNumber);
        Assert.Equal(contract.Key, document.PurchaseContractKey);
        Assert.Equal("PRODUTOR TESTE", document.CardName);
        Assert.False(document.IsBlockedForSettlement);
    }

    [Fact]
    public async Task Receivable_is_not_duplicated_for_the_same_washout()
    {
        var (contract, fixation) = await SeedFixationAsync();
        var washout = WashoutTestData.Washout(contract, fixation, fixedVolume: 10_000m);
        var service = Receivable();

        await service.EnqueueAsync(contract, washout, "tester");
        await _db.Context.SaveChangesAsync();
        await service.EnqueueAsync(contract, washout, "tester");
        await _db.Context.SaveChangesAsync();

        Assert.Single(_db.Context.FinancialDocuments);
    }

    [Fact]
    public async Task Cancel_by_key_refuses_a_settled_document()
    {
        var (contract, fixation) = await SeedFixationAsync();
        var document = WashoutTestData.Provisional(contract, fixation);
        document.SettledAmount = 100m;
        _db.Context.FinancialDocuments.Add(document);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            FinancialDocumentTestServices.Cancel(_db.Context).EnqueueCancelByKeyAsync(document.Key, "estorno", "tester"));

        Assert.Contains("baixa", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cancel_by_key_cancels_an_open_document()
    {
        var (contract, fixation) = await SeedFixationAsync();
        var document = WashoutTestData.Provisional(contract, fixation);
        _db.Context.FinancialDocuments.Add(document);
        await _db.Context.SaveChangesAsync();

        await FinancialDocumentTestServices.Cancel(_db.Context).EnqueueCancelByKeyAsync(document.Key, "estorno", "tester");
        await _db.Context.SaveChangesAsync();

        Assert.Equal(FinancialDocumentStatus.Canceled, _db.Context.FinancialDocuments.Single().Status);
    }

    /// <summary>
    /// F4 (revisão final): a tela manual de cancelamento aceitava QUALQUER documento não
    /// liquidado, inclusive o título a receber do washout. Cancelando por ali, o washout
    /// continuava Approved reduzindo saldo e provisório, e um Estorno depois passava em silêncio
    /// porque EnqueueCancelByKeyAsync trata documento já cancelado como no-op.
    /// </summary>
    [Fact]
    public async Task Manual_cancel_refuses_a_washout_receivable()
    {
        var (contract, fixation) = await SeedFixationAsync();
        var washout = WashoutTestData.Washout(contract, fixation, fixedVolume: 10_000m);
        var receivable = await Receivable().EnqueueAsync(contract, washout, "tester");
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            FinancialDocumentTestServices.Cancel(_db.Context)
                .ExecuteAsync(receivable.Key, "cancelamento manual", "tester"));

        Assert.Contains("washout", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(contract.Code, error.Message);
        Assert.Equal(FinancialDocumentStatus.Open, (await _db.Context.FinancialDocuments.AsNoTracking()
            .SingleAsync(x => x.Key == receivable.Key)).Status);
    }

    /// <summary>
    /// F4: o caso feliz que faltava — o no-op documentado no comentário de
    /// <c>EnqueueCancelByKeyAsync</c> nunca tinha um teste provando que ele de fato não lança.
    /// </summary>
    [Fact]
    public async Task Enqueue_cancel_by_key_on_an_already_canceled_document_is_a_no_op()
    {
        var (contract, fixation) = await SeedFixationAsync();
        var document = WashoutTestData.Provisional(contract, fixation);
        document.Status = FinancialDocumentStatus.Canceled;
        _db.Context.FinancialDocuments.Add(document);
        await _db.Context.SaveChangesAsync();

        await FinancialDocumentTestServices.Cancel(_db.Context).EnqueueCancelByKeyAsync(document.Key, "estorno", "tester");
        await _db.Context.SaveChangesAsync();

        Assert.Equal(FinancialDocumentStatus.Canceled, (await _db.Context.FinancialDocuments.AsNoTracking()
            .SingleAsync(x => x.Key == document.Key)).Status);
    }
}
