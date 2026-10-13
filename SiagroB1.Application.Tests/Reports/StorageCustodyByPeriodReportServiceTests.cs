using SiagroB1.Application.Services.StorageCustody;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

public class StorageCustodyByPeriodReportServiceTests
{
    private static StorageCustodyByPeriodRequest Request(StorageCustodySituationFilter situation = StorageCustodySituationFilter.All) =>
        new() { FromDate = new DateTime(2026, 10, 1), ToDate = new DateTime(2026, 10, 31), Situation = situation };

    private static readonly DateTime Oct10 = new(2026, 10, 10);

    [Fact]
    public async Task Each_romaneio_is_followed_by_its_expected_steps()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync(gross: 30_000m, net: 28_000m, date: Oct10, code: "RM000048");
        await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.ProcessingEntry, 29_500m, DocumentIssuerType.ThirdParty, InvoiceStatus.Confirmed);

        var rows = await new StorageCustodyByPeriodReportService(db, new RecordingFastReportService()).BuildRowsAsync(Request());

        Assert.Equal(4, rows.Count);
        Assert.Equal(StorageCustodyRowKind.Romaneio, rows[0].Kind);
        Assert.Equal("RM000048", rows[0].Transaction);
        Assert.Equal("30.000,000", rows[0].GrossWeightText);
        Assert.Equal("Escriturada", rows[1].Situation);
        Assert.Equal("-500,000", rows[1].DifferenceText);
        Assert.Equal("PENDENTE", rows[2].Situation);
        Assert.Equal("PENDENTE", rows[3].Situation);
        Assert.Equal(1, rows[0].PendingCount);
    }

    [Fact]
    public async Task Lot_balances_compare_physical_and_fiscal_at_the_end_date()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db, requiresProcessing: false);
        var receipt = await seed.AddTransactionAsync(net: 28_000m, date: Oct10);
        var shipment = await seed.AddTransactionAsync(StorageTransactionType.Shipment, net: 10_000m, date: Oct10);
        await seed.AddPurchaseDocumentAsync(receipt, StorageCustodyRole.StorageEntry, 28_000m);
        await seed.AddSalesDocumentAsync(shipment, StorageCustodyRole.StorageReturn, 10_000m);
        await seed.AddTransactionAsync(net: 5_000m, date: new DateTime(2026, 11, 2)); // depois do período: fora

        var rows = await new StorageCustodyByPeriodReportService(db, new RecordingFastReportService()).BuildRowsAsync(Request());

        Assert.All(rows, r => Assert.Equal(18_000m, r.LotPhysicalBalance));
        Assert.All(rows, r => Assert.Equal(18_000m, r.LotFiscalBalance));
        Assert.All(rows, r => Assert.Equal(0m, r.LotDifference));
    }

    [Fact]
    public async Task Situation_filter_keeps_pending_or_complete_romaneios()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db, requiresProcessing: false);
        var complete = await seed.AddTransactionAsync(date: Oct10, code: "RM000001");
        await seed.AddPurchaseDocumentAsync(complete, StorageCustodyRole.StorageEntry, 28_000m);
        await seed.AddTransactionAsync(date: Oct10, code: "RM000002");
        var service = new StorageCustodyByPeriodReportService(db, new RecordingFastReportService());

        var pending = await service.BuildRowsAsync(Request(StorageCustodySituationFilter.Pending));
        var done = await service.BuildRowsAsync(Request(StorageCustodySituationFilter.Complete));

        Assert.Equal(["RM000002"], pending.Where(r => r.Kind == StorageCustodyRowKind.Romaneio).Select(r => r.Transaction));
        Assert.Equal(["RM000001"], done.Where(r => r.Kind == StorageCustodyRowKind.Romaneio).Select(r => r.Transaction));
    }

    [Fact]
    public async Task Romaneio_without_expected_steps_is_left_out()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        await seed.AddTransactionAsync(status: StorageTransactionsStatus.Pending, date: Oct10);

        Assert.Empty(await new StorageCustodyByPeriodReportService(db, new RecordingFastReportService()).BuildRowsAsync(Request()));
    }

    [Fact]
    public async Task Steps_match_the_romaneio_section()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync(date: Oct10);
        await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.ProcessingEntry, 30_000m);

        var rows = await new StorageCustodyByPeriodReportService(db, new RecordingFastReportService()).BuildRowsAsync(Request());
        var summary = await new StorageTransactionsCustodySummaryService(db).ExecuteAsync(t.Key);

        Assert.Equal(
            summary.Steps.Select(s => s.SituationText),
            rows.Where(r => r.Kind == StorageCustodyRowKind.Step).Select(r => r.Situation));
    }
}
