using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Accounting;
using SiagroB1.Application.Services.Accounting.Offline;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.Accounting.Postings.AccountingPostingServiceTests;

namespace SiagroB1.Application.Tests.Accounting.Offline;

/// <summary>Execução da contabilização off-line: uma transação por documento, simulação sem gravar, relatório.</summary>
public class OfflinePostingServiceTests
{
    private static readonly DateTime From = new(2026, 1, 1);
    private static readonly DateTime To = new(2026, 12, 31);
    private static readonly OfflinePostingModule[] Purchases = [OfflinePostingModule.PurchaseInvoices];

    private static async Task<IUnitOfWork> SeedAsync(bool cutover = true)
    {
        var db = await Postings.AccountingPostingServiceTests.SeedAsync(cutover: cutover); // LP 650 ativo
        db.Context.Items.Add(new Item { ItemCode = "10001", ItemName = "SOJA", LedgerAccountCode = "1.1.03" });
        await db.SaveChangesAsync();
        return db;
    }

    private static async Task<PurchaseInvoice> AddPurchase(IUnitOfWork db, string number, DateTime date, string itemCode = "10001")
    {
        var item = InvoiceReportSeed.PurchaseItem(itemCode: itemCode);
        item.MovesFiscalInventory = true;
        var invoice = InvoiceReportSeed.Purchase(number, date, items: item);
        db.Context.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice;
    }

    private static (OfflinePostingService Service, FakeBackgroundJobClient Jobs) Build(IUnitOfWork db,
        params IOfflinePostingSource[] sources)
    {
        var jobs = new FakeBackgroundJobClient();
        IOfflinePostingSource[] all = sources.Length > 0
            ? sources
            : [new PurchaseInvoiceOfflineSource(db), new SalesInvoiceOfflineSource(db), new FinancialOfflineSource(db)];
        return (new OfflinePostingService(db, AccountingTestData.Config(), new AccountingSettingsService(db), all, Service(db),
            jobs, new FakeCompanyContext()), jobs);
    }

    private static async Task<OfflinePostingRun> RunAsync(IUnitOfWork db, bool simulation, params IOfflinePostingSource[] sources)
    {
        var (service, _) = Build(db, sources);
        var run = await service.RequestAsync(From, To, null, Purchases, simulation, "admin", CancellationToken.None);
        await service.ExecuteAsync(run.Key, CancellationToken.None);
        return await db.Context.OfflinePostingRuns.AsNoTracking().Include(r => r.Items).SingleAsync(r => r.Key == run.Key);
    }

    private static Task<int> EntryCount(IUnitOfWork db) => db.Context.JournalEntries.CountAsync();

    [Fact]
    public async Task Process_posts_each_document_in_own_transaction()
    {
        var db = await SeedAsync();
        var good = await AddPurchase(db, "NF1", new DateTime(2026, 7, 10));
        await AddPurchase(db, "NF2", new DateTime(2026, 7, 11), itemCode: "99999"); // produto sem conta → erro no LP

        var run = await RunAsync(db, simulation: false);

        Assert.Equal(OfflinePostingRunStatus.Completed, run.Status);
        Assert.Equal(1, run.PostedCount);
        Assert.Equal(1, run.ErrorCount);
        var entry = await db.Context.JournalEntries.AsNoTracking().SingleAsync();
        Assert.Equal(good.Key.ToString(), entry.SourceDocumentKey);
        Assert.Equal("admin", entry.CreatedBy);
        var posted = run.Items.Single(i => i.DocumentNumber == "NF1");
        Assert.Equal(OfflinePostingItemResult.Posted, posted.Result);
        Assert.Equal(entry.DocEntry.ToString(), posted.JournalEntryDocEntries);
        var error = run.Items.Single(i => i.DocumentNumber == "NF2");
        Assert.Equal(OfflinePostingItemResult.Error, error.Result);
        Assert.Contains("LP 650", error.Message);
    }

    [Fact] // [RF 3]
    public async Task Simulation_writes_no_journal_entries()
    {
        var db = await SeedAsync();
        await AddPurchase(db, "NF1", new DateTime(2026, 7, 10));

        var run = await RunAsync(db, simulation: true);

        Assert.Equal(0, await EntryCount(db));
        Assert.Equal(OfflinePostingItemResult.WouldPost, Assert.Single(run.Items).Result);
        Assert.Equal(1, run.PostedCount);

        // A numeração não foi consumida: o processamento real começa do primeiro número.
        await RunAsync(db, simulation: false);
        Assert.Equal("000001", (await db.Context.JournalEntries.AsNoTracking().SingleAsync()).DocumentNumber);
    }

    [Fact] // [RF 1]
    public async Task Closed_period_document_is_error_others_continue()
    {
        var db = await SeedAsync();
        db.Context.AccountingPeriods.Add(new AccountingPeriod
        {
            Year = 2026, Month = 7, StartDate = new DateTime(2026, 7, 1), EndDate = new DateTime(2026, 7, 31),
            Status = AccountingPeriodStatus.Closed,
        });
        await db.SaveChangesAsync();
        await AddPurchase(db, "NF7", new DateTime(2026, 7, 10));
        await AddPurchase(db, "NF8", new DateTime(2026, 8, 10));

        var run = await RunAsync(db, simulation: false);

        Assert.Equal(OfflinePostingItemResult.Error, run.Items.Single(i => i.DocumentNumber == "NF7").Result);
        Assert.Contains("Período contábil 07/2026 fechado", run.Items.Single(i => i.DocumentNumber == "NF7").Message);
        Assert.Equal(OfflinePostingItemResult.Posted, run.Items.Single(i => i.DocumentNumber == "NF8").Result);
        Assert.Equal(1, await EntryCount(db));
    }

    [Fact] // [RF 2]
    public async Task Second_process_run_finds_nothing()
    {
        var db = await SeedAsync();
        await AddPurchase(db, "NF1", new DateTime(2026, 7, 10));

        await RunAsync(db, simulation: false);
        var second = await RunAsync(db, simulation: false);

        Assert.Empty(second.Items);
        Assert.Equal(1, await EntryCount(db));
    }

    [Fact]
    public async Task Unbalanced_lp_reports_error_with_lp_code()
    {
        var db = await SeedAsync();
        var line = await db.Context.StandardPostingLines.SingleAsync(l => l.Code == "650");
        line.EntryType = JournalEntryLineType.Debit;
        line.CreditAccountSource = null;
        line.CreditAccountCode = null;
        await db.SaveChangesAsync();
        await AddPurchase(db, "NF1", new DateTime(2026, 7, 10));

        var item = Assert.Single((await RunAsync(db, simulation: false)).Items);

        Assert.Equal(OfflinePostingItemResult.Error, item.Result);
        Assert.Contains("650", item.Message);
        Assert.Equal(0, await EntryCount(db));
    }

    [Fact]
    public async Task Request_rejects_without_cutover()
    {
        var db = await SeedAsync(cutover: false);
        var (service, _) = Build(db);
        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            service.RequestAsync(From, To, null, Purchases, false, "admin", CancellationToken.None));
        Assert.Equal("Defina a data de corte da contabilidade antes da contabilização off-line.", e.Message);
    }

    [Fact]
    public async Task Request_rejects_in_sapb1()
    {
        var db = await SeedAsync();
        var service = new OfflinePostingService(db, AccountingTestData.Config("SAPB1"), new AccountingSettingsService(db), [],
            Service(db), new FakeBackgroundJobClient(), new FakeCompanyContext());
        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            service.RequestAsync(From, To, null, Purchases, false, "admin", CancellationToken.None));
        Assert.Equal("Disponível somente no modo STANDALONE.", e.Message);
    }

    [Fact]
    public async Task Request_rejects_when_run_in_progress()
    {
        var db = await SeedAsync();
        var (service, jobs) = Build(db);
        await service.RequestAsync(From, To, null, Purchases, false, "admin", CancellationToken.None);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            service.RequestAsync(From, To, null, Purchases, true, "admin", CancellationToken.None));
        Assert.Equal("Já existe uma contabilização off-line em andamento.", e.Message);
        Assert.Single(jobs.Enqueued);
        Assert.Equal(["0001"], jobs.EnqueuedCompanyCodes);
    }

    [Fact]
    public async Task Request_validates_range_and_modules()
    {
        var db = await SeedAsync();
        var (service, _) = Build(db);
        await Assert.ThrowsAsync<DefaultException>(() =>
            service.RequestAsync(To, From, null, Purchases, false, "admin", CancellationToken.None));
        await Assert.ThrowsAsync<DefaultException>(() =>
            service.RequestAsync(From, To, null, [], false, "admin", CancellationToken.None));
    }

    [Fact]
    public async Task Request_clamps_from_to_cutover()
    {
        var db = await SeedAsync();
        var (service, _) = Build(db);
        var run = await service.RequestAsync(new DateTime(2025, 6, 1), To, null, Purchases, false, "admin", CancellationToken.None);
        Assert.Equal(new DateTime(2026, 1, 1), run.DateFrom);
        Assert.Equal(OfflinePostingRunStatus.Queued, run.Status);
    }

    [Fact]
    public async Task Items_are_in_chronological_order()
    {
        var db = await SeedAsync();
        await AddPurchase(db, "NF3", new DateTime(2026, 9, 1));
        await AddPurchase(db, "NF1", new DateTime(2026, 3, 1));
        await AddPurchase(db, "NF2", new DateTime(2026, 5, 1));

        var run = await RunAsync(db, simulation: true);

        Assert.Equal(["NF1", "NF2", "NF3"], run.Items.OrderBy(i => i.Sequence).Select(i => i.DocumentNumber));
    }

    [Fact]
    public async Task Unexpected_exception_marks_item_error_not_run_failed()
    {
        var db = await SeedAsync();
        await AddPurchase(db, "NF1", new DateTime(2026, 7, 10));

        var run = await RunAsync(db, simulation: false, new ThrowingSource(), new PurchaseInvoiceOfflineSource(db));

        Assert.Equal(OfflinePostingRunStatus.Completed, run.Status);
        Assert.Equal("LP 650: boom", run.Items.Single(i => i.DocumentNumber == "X1").Message);
        Assert.Equal(OfflinePostingItemResult.Posted, run.Items.Single(i => i.DocumentNumber == "NF1").Result);
    }

    [Fact]
    public async Task Informative_event_is_skipped()
    {
        var db = await SeedAsync();
        var invoice = InvoiceReportSeed.Purchase("NFC", new DateTime(2026, 7, 10), InvoiceStatus.Cancelled);
        db.Context.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync();

        var run = await RunAsync(db, simulation: false);

        var item = Assert.Single(run.Items);
        Assert.Equal(OfflinePostingItemResult.Skipped, item.Result);
        Assert.Equal(OfflinePostingLookup.NoNetEffect, item.Message);
        Assert.Equal(1, run.SkippedCount);
    }

    [Fact]
    public async Task Canceled_execution_marks_run_failed()
    {
        var db = await SeedAsync();
        await AddPurchase(db, "NF1", new DateTime(2026, 7, 10));
        var (service, _) = Build(db);
        var run = await service.RequestAsync(From, To, null, Purchases, false, "admin", CancellationToken.None);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExecuteAsync(run.Key, cts.Token));

        var stored = await db.Context.OfflinePostingRuns.AsNoTracking().SingleAsync();
        Assert.Equal(OfflinePostingRunStatus.Failed, stored.Status);
        Assert.Equal("Execução interrompida.", stored.FailureMessage);
    }

    [Fact]
    public async Task Stale_running_run_does_not_block_new_request()
    {
        var db = await SeedAsync();
        db.Context.OfflinePostingRuns.Add(new OfflinePostingRun
        {
            DateFrom = From, DateTo = To, Modules = "1", RequestedBy = "x", Status = OfflinePostingRunStatus.Running,
            RequestedAt = DateTime.Now.AddHours(-7), StartedAt = DateTime.Now.AddHours(-7),
        });
        await db.SaveChangesAsync();
        var (service, _) = Build(db);

        var run = await service.RequestAsync(From, To, null, Purchases, false, "admin", CancellationToken.None);

        Assert.Equal(OfflinePostingRunStatus.Queued, run.Status);
        Assert.Equal(OfflinePostingRunStatus.Failed,
            (await db.Context.OfflinePostingRuns.AsNoTracking().SingleAsync(r => r.RequestedBy == "x")).Status);
    }

    private sealed class ThrowingSource : IOfflinePostingSource
    {
        public OfflinePostingModule Module => OfflinePostingModule.PurchaseInvoices;

        public async IAsyncEnumerable<OfflinePostingEvent> CollectAsync(DateTime from, DateTime to, string? branchCode,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield return new OfflinePostingEvent(new DateTime(2026, 6, 1), "Fake", "K", "X1", "01", ["650"], null,
                (_, _, _) => throw new InvalidOperationException("boom"));
        }
    }
}
