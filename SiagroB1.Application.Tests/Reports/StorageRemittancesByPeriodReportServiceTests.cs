using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

public class StorageRemittancesByPeriodReportServiceTests
{
    private static readonly DateTime Oct01 = new(2026, 10, 1);
    private static readonly DateTime Oct10 = new(2026, 10, 10);
    private static readonly DateTime Oct11 = new(2026, 10, 11);
    private static readonly DateTime Oct12 = new(2026, 10, 12);
    private static readonly DateTime Oct31 = new(2026, 10, 31);

    [Fact]
    public async Task BuildRows_OneRowPerRemittanceWithTheFourQuantities()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db, released: 100_000m);
        var remittance = await seed.AddRemittanceAsync(100_000m, date: Oct10, number: "000000123");
        var ret = await seed.AddReturnAsync(remittance, 29_500m, InvoiceStatus.Confirmed);
        await seed.AddReturnAsync(remittance, 50_000m, InvoiceStatus.Cancelled);
        var s1 = await seed.AddShipmentAsync(30_000m);
        await seed.AddShipmentAsync(20_000m);
        await Reconcile(db, ret, s1);

        var row = (await Service(db).BuildRowsAsync(Request())).Single();

        Assert.Equal(StorageRemittanceRowKind.Remittance, row.Kind);
        Assert.Equal("000000123", row.Remittance);
        Assert.Equal("10/10/2026", row.InvoiceDate);
        Assert.Equal("Confirmado", row.Status);
        Assert.Equal("", row.NfeStatus);
        Assert.Equal("ARM001", row.Warehouse);
        Assert.Equal("00000001", row.Contract);
        Assert.Equal("SOJA - KG", row.Group);
        Assert.Equal("KG", row.UnitOfMeasure);
        Assert.Equal(100_000m, row.RemittedQuantity);
        Assert.Equal(29_500m, row.ReturnedQuantity);
        Assert.Equal(70_500m, row.PendingReturnQuantity);
        Assert.Equal(20_000m, row.ShippedWithoutReturnQuantity);
        Assert.Equal("100.000,000", row.RemittedText);
        Assert.Equal("29.500,000", row.ReturnedText);
        Assert.Equal("70.500,000", row.PendingReturnText);
        Assert.Equal("20.000,000", row.ShippedWithoutReturnText);
        Assert.Equal(1, row.RemittanceCount);
        Assert.Equal("", row.Detail);
    }

    [Fact]
    public async Task BuildRows_SkipsCancelledRemittancesAndKeepsPendingOnes()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        await seed.AddRemittanceAsync(10m, InvoiceStatus.Cancelled, date: Oct10, number: "000000001");
        var pending = await seed.AddRemittanceAsync(20m, InvoiceStatus.Pending, date: Oct10, number: "000000002");
        pending.NfeStatus = NfeStatus.Rejected;
        await db.SaveChangesAsync();

        var row = (await Service(db).BuildRowsAsync(Request())).Single();

        Assert.Equal("000000002", row.Remittance);
        Assert.Equal("Pendente", row.Status);
        Assert.Equal("Rejeitada", row.NfeStatus);
    }

    [Fact]
    public async Task BuildRows_FiltersByRemittanceDateAndCountsReturnsIssuedLater()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var inside = await seed.AddRemittanceAsync(100m, date: new DateTime(2026, 10, 31, 23, 0, 0), number: "000000001");
        await seed.AddRemittanceAsync(100m, release: await seed.AddReleaseAsync(), date: new DateTime(2026, 11, 1), number: "000000002");
        await seed.AddRemittanceAsync(100m, release: await seed.AddReleaseAsync(), date: new DateTime(2026, 9, 30), number: "000000003");
        await seed.AddReturnAsync(inside, 100m, InvoiceStatus.Confirmed, date: new DateTime(2026, 11, 5));

        var row = (await Service(db).BuildRowsAsync(Request())).Single();

        Assert.Equal("000000001", row.Remittance);
        Assert.Equal(0m, row.PendingReturnQuantity);
    }

    [Theory]
    [InlineData("BranchCode")]
    [InlineData("ItemCode")]
    [InlineData("CardCode")]
    public async Task BuildRows_EachOptionalFilterRestrictsTheResult(string filter)
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        await seed.AddRemittanceAsync(100m, date: Oct10, number: "000000001");
        var other = await seed.AddRemittanceAsync(100m, release: await seed.AddReleaseAsync(), date: Oct10, number: "000000002");
        other.BranchCode = "99";
        other.CardCode = "ARM999";
        other.Items.Single().ItemCode = "MILHO";
        await db.SaveChangesAsync();

        var request = Request();
        switch (filter)
        {
            case "BranchCode": request.BranchCode = StorageDepositSeed.Branch; break;
            case "ItemCode": request.ItemCode = StorageDepositSeed.Item; break;
            case "CardCode": request.CardCode = StorageDepositSeed.Warehouse; break;
        }

        var rows = await Service(db).BuildRowsAsync(request);

        Assert.Equal(new[] { "000000001" }, rows.Select(r => r.Remittance));
    }

    [Theory]
    [InlineData(StorageRemittanceSituation.All, new[] { "000000001", "000000002", "000000003" })]
    [InlineData(StorageRemittanceSituation.Open, new[] { "000000001" })]
    [InlineData(StorageRemittanceSituation.Returned, new[] { "000000002", "000000003" })]
    public async Task BuildRows_SituationSplitsOpenFromFullyReturned(StorageRemittanceSituation situation, string[] expected)
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        await seed.AddRemittanceAsync(100m, date: Oct10, number: "000000001");
        var exact = await seed.AddRemittanceAsync(100m, release: await seed.AddReleaseAsync(), date: Oct10, number: "000000002");
        await seed.AddReturnAsync(exact, 100m, InvoiceStatus.Confirmed);
        var excess = await seed.AddRemittanceAsync(100m, release: await seed.AddReleaseAsync(), date: Oct10, number: "000000003");
        await seed.AddReturnAsync(excess, 120m, InvoiceStatus.Confirmed);

        var request = Request();
        request.ReturnSituation = situation;
        var rows = await Service(db).BuildRowsAsync(request);

        Assert.Equal(expected, rows.Select(r => r.Remittance));
        if (situation == StorageRemittanceSituation.Returned)
            Assert.Equal("-20,000", rows.Single(r => r.Remittance == "000000003").PendingReturnText);
    }

    [Fact]
    public async Task BuildRows_CountsOnlyTheLoadingTransactionsOfEachOrigin()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        await seed.AddRemittanceAsync(100_000m, date: Oct10, number: "000000001");
        await seed.AddShipmentAsync(30_000m);                                                       // Compra(8): conta
        await seed.AddShipmentAsync(5_000m, origin: TransactionCode.WarehouseReconciliation);      // conferência: fora
        await seed.AddShipmentAsync(7_000m, StorageTransactionType.SalesShipment);                 // Saída(7) em Standard: fora
        var cancelled = await seed.AddShipmentAsync(17_000m);
        cancelled.TransactionStatus = StorageTransactionsStatus.Cancelled;                           // cancelado: fora
        await db.SaveChangesAsync();
        var transfer = await seed.AddReleaseAsync(ReleaseOrigin.OwnershipTransfer);
        await seed.AddRemittanceAsync(50_000m, release: transfer, date: Oct10, number: "000000002");
        await seed.AddShipmentAsync(11_000m, StorageTransactionType.SalesShipment, release: transfer); // conta
        await seed.AddShipmentAsync(13_000m, release: transfer);                                     // Compra sem perna: fora

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { 30_000m, 11_000m }, rows.Select(r => r.ShippedWithoutReturnQuantity));
    }

    [Fact]
    public async Task BuildRows_GroupsByProductAndOrdersByDateThenNumber()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        await seed.AddRemittanceAsync(1m, date: Oct11, number: "000000001");
        await seed.AddRemittanceAsync(1m, release: await seed.AddReleaseAsync(), date: Oct10, number: "000000009");
        await seed.AddRemittanceAsync(1m, release: await seed.AddReleaseAsync(), date: Oct10, number: "000000005");
        var milho = await seed.AddRemittanceAsync(1m, release: await seed.AddReleaseAsync(), date: Oct12, number: "000000007");
        milho.Items.Single().ItemCode = "MILHO";
        await db.SaveChangesAsync();

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "MILHO - KG", "SOJA - KG", "SOJA - KG", "SOJA - KG" }, rows.Select(r => r.Group));
        Assert.Equal(new[] { "000000007", "000000005", "000000009", "000000001" }, rows.Select(r => r.Remittance));
    }

    [Fact]
    public void Queries_TranslateToSqlServer()
    {
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none")
            .Options);
        var request = Request();
        request.BranchCode = "01";
        request.ItemCode = "SOJA";
        request.CardCode = "ARM001";
        var remittances = StorageRemittanceData.RemittancesQuery(context, request);

        var sql = new[]
        {
            remittances.ToQueryString(),
            StorageRemittanceData.ReturnLinesQuery(context, StorageRemittanceData.RemittanceItemKeys(remittances)).ToQueryString(),
            StorageRemittanceData.TransactionsQuery(context, StorageRemittanceData.ReleaseKeys(remittances)).ToQueryString(),
        };

        Assert.Contains("[SALES_INVOICES]", sql[0]);
        // O tradutor quebra a linha depois do "IN (": aceita espaço em branco antes do SELECT.
        Assert.All(sql[1..], s => Assert.Matches(@"IN \(\s*SELECT", s));
        Assert.Contains("[PURCHASE_INVOICES_ITEMS]", sql[1]);
        Assert.Contains("[STORAGE_RETURN_RECONCILIATIONS]", sql[2]);
    }

    private static StorageRemittancesByPeriodReportService Service(IUnitOfWork db) =>
        new(db, new RecordingFastReportService());

    private static StorageRemittancesByPeriodRequest Request() => new() { FromDate = Oct01, ToDate = Oct31 };

    private static async Task Reconcile(UnitOfWork db, PurchaseInvoice ret, StorageTransaction transaction)
    {
        db.Context.StorageReturnReconciliations.Add(new StorageReturnReconciliation
        {
            PurchaseInvoiceKey = ret.Key, StorageTransactionKey = transaction.Key,
        });
        await db.SaveChangesAsync();
    }
}
