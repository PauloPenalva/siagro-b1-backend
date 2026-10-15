using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.SalesContracts.Washouts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Spec 2026-10-15 (washout de venda) §5.2, §5.5, §5.6 e §6: as peças financeiras do lado de venda.</summary>
public class FinancialDocumentsSalesWashoutTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private FinancialDocumentsGenerateService Generate() => FinancialDocumentTestServices.Generate(_db.Context);

    private FinancialDocumentsAdjustProvisionalService Adjust() =>
        new(_db.Context, Generate(), new FinancialDocumentChangeLogService(_db.Context));

    private FinancialDocumentsGenerateWashoutReceivableService Receivable() => new(
        _db.Context, new FakeDocNumberSequenceService(),
        new FakeBusinessPartnerService(new Dictionary<string, string> { ["C0001"] = "CLIENTE TESTE" }));

    private async Task<(SalesContract Contract, SalesContractPriceFixation Fixation)> SeedAsync(
        params (decimal FixedVolume, SalesContractWashoutStatus Status)[] washouts)
    {
        var contract = SalesWashoutTestData.Contract();
        var fixation = SalesWashoutTestData.Fixation(contract, 1_000m);
        _db.Context.SalesContracts.Add(contract);
        _db.Context.SalesContractsPriceFixations.Add(fixation);
        var sequence = 1;
        foreach (var (fixedVolume, status) in washouts)
            _db.Context.SalesContractsWashouts.Add(
                SalesWashoutTestData.Washout(contract, fixation, fixedVolume: fixedVolume, status: status, sequence: sequence++));
        await _db.Context.SaveChangesAsync();
        return (contract, fixation);
    }

    private IQueryable<FinancialDocument> Receivables() =>
        _db.Context.FinancialDocuments.AsNoTracking().Where(d => d.Direction == FinancialDirection.Receivable);

    [Fact]
    public async Task Sales_generator_discounts_approved_washouts()
    {
        var (contract, fixation) = await SeedAsync(
            (400m, SalesContractWashoutStatus.Approved),
            (100m, SalesContractWashoutStatus.InApproval),
            (50m, SalesContractWashoutStatus.Reversed));

        await Generate().EnqueueForSalesFixationAsync(contract, fixation, "tester");
        await _db.Context.SaveChangesAsync();

        // (1.000 − 400) × 100
        Assert.Equal(60_000m, Assert.Single(Receivables()).NetAmount);
    }

    [Fact]
    public async Task Sales_generator_writes_nothing_for_a_fully_washed_fixation()
    {
        var (contract, fixation) = await SeedAsync((1_000m, SalesContractWashoutStatus.Approved));

        await Generate().EnqueueForSalesFixationAsync(contract, fixation, "tester");
        await _db.Context.SaveChangesAsync();

        Assert.Empty(Receivables());
    }

    [Fact]
    public async Task Fully_washed_sales_fixation_is_skipped_by_the_backlog()
    {
        await SeedAsync((1_000m, SalesContractWashoutStatus.Approved));

        var result = await new FinancialDocumentsGenerateBacklogService(_db, Generate()).ExecuteAsync(
            branchCode: null, fromDate: DateTime.Today.AddYears(-1), toDate: DateTime.Today.AddDays(1),
            dryRun: false, userName: "tester");

        Assert.Equal(1, result.SkippedFullyWashedOut);
        Assert.Equal(0, result.Generated);
        Assert.Empty(Receivables());
    }

    [Fact]
    public async Task Adjust_reduces_the_sales_provisional_in_place_and_logs_it()
    {
        var (contract, fixation) = await SeedAsync();
        var provisional = SalesWashoutTestData.Provisional(contract, fixation);
        _db.Context.FinancialDocuments.Add(provisional);
        await _db.Context.SaveChangesAsync();

        await Adjust().EnqueueForSalesFixationAsync(contract, fixation, 600m, "tester");
        await _db.Context.SaveChangesAsync();

        var reloaded = Assert.Single(Receivables());
        Assert.Equal(60_000m, reloaded.NetAmount);
        Assert.Equal(FinancialDocumentStatus.Open, reloaded.Status);
        Assert.Equal(FinancialDocumentChangeLogFields.NetAmount, _db.Context.FinancialDocumentChangeLogs.Single().Field);
    }

    [Fact]
    public async Task Adjust_to_zero_cancels_the_sales_provisional()
    {
        var (contract, fixation) = await SeedAsync();
        _db.Context.FinancialDocuments.Add(SalesWashoutTestData.Provisional(contract, fixation));
        await _db.Context.SaveChangesAsync();

        await Adjust().EnqueueForSalesFixationAsync(contract, fixation, 0m, "tester");
        await _db.Context.SaveChangesAsync();

        var cancelled = Assert.Single(Receivables());
        Assert.Equal(FinancialDocumentStatus.Canceled, cancelled.Status);
        Assert.Equal("Volume lavado por washout", cancelled.CancellationReason);
    }

    [Fact]
    public async Task Adjust_below_the_composed_amount_is_refused()
    {
        var (contract, fixation) = await SeedAsync();
        var provisional = SalesWashoutTestData.Provisional(contract, fixation);
        provisional.ComposedAmount = 70_000m;
        _db.Context.FinancialDocuments.Add(provisional);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            Adjust().EnqueueForSalesFixationAsync(contract, fixation, 600m, "tester"));

        Assert.Equal(
            "O provisório FN-PROV-V1 já tem 70.000,00 composto: o washout o deixaria em 60.000,00. Desfaça a composição antes de aprovar.",
            error.Message);
    }

    [Fact]
    public async Task Adjust_without_an_open_provisional_generates_one_with_the_remaining_volume()
    {
        var (contract, fixation) = await SeedAsync();

        await Adjust().EnqueueForSalesFixationAsync(contract, fixation, 700m, "tester");
        await _db.Context.SaveChangesAsync();

        Assert.Equal(70_000m, Assert.Single(Receivables()).NetAmount);
    }

    [Fact]
    public async Task Receivable_is_a_firm_from_the_sales_washout_origin()
    {
        var (contract, fixation) = await SeedAsync();
        var washout = SalesWashoutTestData.Washout(contract, fixation, fixedVolume: 100m);

        var document = await Receivable().EnqueueAsync(contract, washout, "tester");

        Assert.Equal(FinancialDirection.Receivable, document.Direction);
        Assert.Equal(FinancialDocumentNature.Firm, document.Nature);
        Assert.Equal(FinancialDocumentOrigin.SalesContractWashout, document.OriginType);
        Assert.Equal(washout.Key, document.OriginKey);
        Assert.Equal(contract.Code, document.OriginDocNumber);
        Assert.Equal(contract.Key, document.SalesContractKey);
        Assert.Null(document.PurchaseContractKey);
        Assert.Equal(1_500m, document.NetAmount);   // (100 − 90) × 100 + 500
        Assert.Equal(new DateTime(2026, 10, 31), document.DueDate);
        Assert.False(document.IsSalesFirmReceivable);
        Assert.False(document.IsBlockedForSettlement);
    }

    [Fact]
    public async Task Washout_receivable_cannot_be_cancelled_on_the_financial_screen()
    {
        var (contract, fixation) = await SeedAsync();
        var document = await Receivable().EnqueueAsync(
            contract, SalesWashoutTestData.Washout(contract, fixation, fixedVolume: 100m), "tester");
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            FinancialDocumentTestServices.Cancel(_db.Context).ExecuteAsync(document.Key, "teste", "tester"));

        Assert.Equal(
            "Título gerado por washout não pode ser cancelado aqui. Estorne o washout no contrato de venda CV-001.",
            error.Message);
    }
}
