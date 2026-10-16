using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.ContractPositionSeed;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>"Hoje" nestes testes é <see cref="ContractPositionSeed.Today"/> = 08/10/2026.</summary>
public class DashboardServiceTests
{
    [Fact]
    public async Task Build_EmptyDatabase_ReturnsEmptySectionsAndSevenDays()
    {
        var db = TestDb.CreateUnitOfWork();

        var dto = await Service(db).BuildAsync(Today);

        Assert.Equal(Today, dto.GeneratedAt.Date);
        Assert.Equal(0, dto.Financial.PayablesDueToday.Count);
        Assert.Empty(dto.Stock);
        Assert.Empty(dto.MonthlyPosition);
        Assert.Equal(7, dto.RecentLoads.Count);
        Assert.Equal(0, dto.SalesToDeliver.Total);
    }

    // ---------- Financeiro (§4.1) ----------

    [Fact]
    public async Task Financial_TodayAndOverdueSplitByDirection()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.FinancialDocuments.AddRange(
            Fin(FinancialDirection.Payable, Today, 1000m, settled: 200m),
            Fin(FinancialDirection.Payable, Today.AddDays(-1), 500m),
            Fin(FinancialDirection.Receivable, Today, 300m),
            Fin(FinancialDirection.Receivable, Today.AddDays(1), 900m));
        await Save(db);

        var f = (await Service(db).BuildAsync(Today)).Financial;

        Assert.Equal((800m, 1), (f.PayablesDueToday.Amount, f.PayablesDueToday.Count));
        Assert.Equal((500m, 1), (f.PayablesOverdue.Amount, f.PayablesOverdue.Count));
        Assert.Equal((300m, 1), (f.ReceivablesDueToday.Amount, f.ReceivablesDueToday.Count));
        Assert.Equal((0m, 0), (f.ReceivablesOverdue.Amount, f.ReceivablesOverdue.Count));
    }

    [Fact]
    public async Task Financial_SettledCanceledAndAdvanceAreOut()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.FinancialDocuments.AddRange(
            Fin(FinancialDirection.Payable, Today, 100m, status: FinancialDocumentStatus.Settled),
            Fin(FinancialDirection.Payable, Today, 100m, status: FinancialDocumentStatus.Canceled),
            Fin(FinancialDirection.Payable, Today, 100m, nature: FinancialDocumentNature.Advance));
        await Save(db);

        var f = (await Service(db).BuildAsync(Today)).Financial;

        Assert.Equal((0m, 0), (f.PayablesDueToday.Amount, f.PayablesDueToday.Count));
    }

    [Fact]
    public async Task Financial_OtherCurrencyCountedNotSummed()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.FinancialDocuments.AddRange(
            Fin(FinancialDirection.Payable, Today, 1000m),
            Fin(FinancialDirection.Payable, Today, 400m, currency: CurrencyType.Usd));
        await Save(db);

        var f = (await Service(db).BuildAsync(Today)).Financial;

        Assert.Equal((1000m, 2), (f.PayablesDueToday.Amount, f.PayablesDueToday.Count));
    }

    // Decisão do usuário: o cartão mostra o compromisso do contrato, não só o que já pode ser liberado.
    [Fact]
    public async Task Financial_ProvisionalWithoutInvoiceCounts()
    {
        var db = TestDb.CreateUnitOfWork();
        var doc = Fin(FinancialDirection.Payable, Today, 5000m);
        doc.ComposedAmount = 0m;
        db.Context.FinancialDocuments.Add(doc);
        await Save(db);

        var f = (await Service(db).BuildAsync(Today)).Financial;

        Assert.Equal(5000m, f.PayablesDueToday.Amount);
    }

    private static FinancialDocument Fin(
        FinancialDirection dir, DateTime due, decimal net, decimal settled = 0m,
        FinancialDocumentStatus status = FinancialDocumentStatus.Open,
        FinancialDocumentNature nature = FinancialDocumentNature.Provisional,
        CurrencyType currency = CurrencyType.Brl) => new()
    {
        Key = Guid.NewGuid(),
        CardCode = "F001",
        Direction = dir,
        DueDate = due,
        NetAmount = net,
        SettledAmount = settled,
        Status = status,
        Nature = nature,
        Currency = currency,
    };

    private static DashboardService Service(IUnitOfWork db) =>
        new(db, new ContractMonthlyPositionReportService(db, new RecordingFastReportService()));

    private static Task Save(IUnitOfWork db) => db.Context.SaveChangesAsync();
}
