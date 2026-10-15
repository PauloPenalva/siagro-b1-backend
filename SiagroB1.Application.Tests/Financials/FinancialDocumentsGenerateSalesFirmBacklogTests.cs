using Microsoft.EntityFrameworkCore;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Enums;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Spec 2026-10-15 regra 19: firmes dos documentos de saída confirmados antes do deploy.</summary>
public class FinancialDocumentsGenerateSalesFirmBacklogTests
{
    private static FinancialDocumentsGenerateSalesFirmBacklogService Service(SalesFirmReceivableSeed seed) =>
        new(seed.Db, seed.Sync());

    private static readonly DateTime From = new(2026, 10, 1);
    private static readonly DateTime To = new(2026, 10, 31);

    [Fact]
    public async Task Dry_run_counts_the_lines_without_firm_and_writes_nothing()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed);

        var result = await Service(seed).ExecuteAsync(null, From, To, dryRun: true, "ana");

        Assert.True(result.DryRun);
        Assert.Equal(1, result.EligibleLines);
        Assert.Equal(1, result.LinesWithoutFirm);
        Assert.Equal(0, result.Generated);
        Assert.False(await seed.Context.FinancialDocuments.AnyAsync(d => d.OriginType == FinancialDocumentOrigin.SalesInvoice));
    }

    [Fact]
    public async Task Run_generates_the_missing_firms_and_is_idempotent()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (_, line) = await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed);

        var first = await Service(seed).ExecuteAsync(null, From, To, dryRun: false, "ana");
        var second = await Service(seed).ExecuteAsync(null, From, To, dryRun: false, "ana");

        Assert.Equal(1, first.Generated);
        Assert.Equal(0, second.Generated);
        Assert.Equal(1, second.SkippedAlreadyGenerated);
        Assert.Single(await seed.TitlesOfAsync(line));
    }

    [Fact]
    public async Task Lines_outside_the_window_branch_or_of_finished_contracts_are_ignored()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var finished = await seed.AddContractAsync("VC-9");
        var tracked = await seed.Context.SalesContracts.SingleAsync(c => c.Key == finished.Key);
        tracked.Status = ContractStatus.Finished;
        await seed.Context.SaveChangesAsync();
        await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed, contract: finished);
        var (outside, _) = await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed, number: "DS000009");
        var trackedInvoice = await seed.Context.SalesInvoices.SingleAsync(i => i.Key == outside.Key);
        trackedInvoice.InvoiceDate = new DateTime(2026, 9, 30);
        await seed.Context.SaveChangesAsync();

        var result = await Service(seed).ExecuteAsync("01", From, To, dryRun: false, "ana");

        Assert.Equal(0, result.EligibleLines);
    }

    [Fact]
    public void Edm_has_the_backlog_action()
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        var model = builder.GetEdmModel();

        Assert.Contains(model.SchemaElements, e => e.Name == "FinancialDocumentsGenerateSalesFirmBacklog");
    }
}
