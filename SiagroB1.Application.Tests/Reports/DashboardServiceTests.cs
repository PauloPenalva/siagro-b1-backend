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

    // ---------- Contratos vencidos (§4.1) e filas (§4.5) ----------

    [Fact]
    public async Task Overdue_CountsOnlyPastDeadlineWithBalance()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseContracts.AddRange(
            Purchase("PC000001", total: 1000m, deliveryEnd: Today.AddDays(-1)),
            Purchase("PC000002", total: 1000m, deliveryEnd: Today),
            Purchase("PC000003", total: 1000m, deliveryEnd: Today.AddDays(-1), status: ContractStatus.Finished));
        db.Context.SalesContracts.Add(Sales("CV000001", total: 300m, deliveryEnd: Today.AddDays(-5)));
        await Save(db);

        var o = (await Service(db).BuildAsync(Today)).OverdueContracts;

        Assert.Equal(1, o.Purchase.Count);
        Assert.Equal(("KG", 1000m), (o.Purchase.Quantities.Single().UnitOfMeasure, o.Purchase.Quantities.Single().Quantity));
        Assert.Equal(1, o.Sales.Count);
        Assert.Equal(300m, o.Sales.Quantities.Single().Quantity);
    }

    [Fact]
    public async Task Overdue_NegativeBalanceIsNotOverdue()
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = Purchase("PC000001", total: 1000m, deliveryEnd: Today.AddDays(-1));
        db.Context.PurchaseContracts.Add(contract);
        db.Context.PurchaseContractsAllocations.Add(PurchaseAllocation(contract, 1200m));
        await Save(db);

        var o = (await Service(db).BuildAsync(Today)).OverdueContracts;

        Assert.Equal(0, o.Purchase.Count);
    }

    [Fact]
    public async Task Overdue_QuantitiesGroupedByUom()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseContracts.AddRange(
            Purchase("PC000001", total: 1000m, deliveryEnd: Today.AddDays(-1), uom: "KG"),
            Purchase("PC000002", total: 500m, deliveryEnd: Today.AddDays(-1), uom: "kg "));
        await Save(db);

        var o = (await Service(db).BuildAsync(Today)).OverdueContracts;

        var q = Assert.Single(o.Purchase.Quantities);
        Assert.Equal(("KG", 1500m), (q.UnitOfMeasure, q.Quantity));
    }

    [Fact]
    public async Task Queues_OrderedByCashFlowThenDeliveryEndNullsLast_TopTenWithTotal()
    {
        var db = TestDb.CreateUnitOfWork();
        var contracts = new List<SalesContract>();
        for (var i = 0; i < 9; i++)
            contracts.Add(Sales($"CV0000{i + 1:00}", total: 100m, cashFlow: Today.AddDays(20 - i), deliveryEnd: Today.AddDays(30)));
        contracts.Add(Sales("CV000010", total: 100m, cashFlow: Today.AddDays(1), deliveryEnd: DateTime.MinValue)); // sem prazo
        contracts.Add(Sales("CV000011", total: 100m, cashFlow: null, deliveryEnd: Today.AddDays(2)));
        contracts.Add(Sales("CV000012", total: 100m, cashFlow: null, deliveryEnd: Today.AddDays(3)));
        db.Context.SalesContracts.AddRange(contracts);
        await Save(db);

        var q = (await Service(db).BuildAsync(Today)).SalesToDeliver;

        Assert.Equal(12, q.Total);
        Assert.Equal(10, q.Items.Count);
        Assert.Equal("CV000010", q.Items[0].Code);                   // menor recebimento
        Assert.Null(q.Items[0].DeliveryEndDate);                      // sem prazo sai nulo
        Assert.DoesNotContain(q.Items, i => i.CashFlowDate is null);  // os sem previsão ficam para o fim
        Assert.Equal(q.Items.Select(i => i.CashFlowDate!.Value).OrderBy(d => d), q.Items.Select(i => i.CashFlowDate!.Value));
    }

    [Fact]
    public async Task Queues_ExcludeZeroBalanceAndFlagOverdue()
    {
        var db = TestDb.CreateUnitOfWork();
        var delivered = Sales("CV000001", total: 100m, deliveryEnd: Today.AddDays(10));
        var overdue = Sales("CV000002", total: 100m, deliveryEnd: Today.AddDays(-1));
        var item = SalesItem(100m, closed: true, delivered: 100m);
        db.Context.SalesContracts.AddRange(delivered, overdue);
        db.Context.SalesInvoicesItems.Add(item);
        db.Context.SalesContractsAllocations.Add(SalesAllocation(delivered, item, 100m));
        await Save(db);

        var q = (await Service(db).BuildAsync(Today)).SalesToDeliver;

        var only = Assert.Single(q.Items);
        Assert.Equal(("CV000002", true, 100m), (only.Code, only.IsOverdue, only.ToDeliver));
        Assert.Equal(1, q.Total);
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
