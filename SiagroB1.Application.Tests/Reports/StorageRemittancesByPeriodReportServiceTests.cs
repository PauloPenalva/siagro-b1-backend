using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.StorageDeposits;
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

        var row = Remittances(await Service(db).BuildRowsAsync(Request())).Single();

        Assert.Equal(StorageRemittanceRowKind.Remittance, row.Kind);
        Assert.Equal("000000123", row.TaxDocument);
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

        var row = Remittances(await Service(db).BuildRowsAsync(Request())).Single();

        Assert.Equal("000000002", row.TaxDocument);
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

        var row = Remittances(await Service(db).BuildRowsAsync(Request())).Single();

        Assert.Equal("000000001", row.TaxDocument);
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

        Assert.Equal(new[] { "000000001" }, rows.Select(r => r.TaxDocument));
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

        Assert.Equal(expected, Remittances(rows).Select(r => r.TaxDocument));
        if (situation == StorageRemittanceSituation.Returned)
            Assert.Equal("-20,000", Remittances(rows).Single(r => r.TaxDocument == "000000003").PendingReturnText);
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
        Assert.Equal(new[] { "000000007", "000000005", "000000009", "000000001" }, rows.Select(r => r.TaxDocument));
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

    private const string RemittanceKey = "35261012345678000195550010000000381000000381";
    private const string ReturnKey = "35261098765432000110550010000007901000007901";

    [Fact]
    public async Task BuildRows_RemittanceRowShowsInternalNumberTaxDocumentAndAccessKey()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100m, date: Oct10, number: "000000038");
        remittance.InvoiceNumber = "DS000176";
        remittance.TaxDocumentSeries = "1";
        remittance.ChaveNFe = RemittanceKey;
        await db.SaveChangesAsync();

        var row = (await Service(db).BuildRowsAsync(Request())).Single();

        Assert.Equal("DS000176", row.Document);
        Assert.Equal("000000038-1", row.TaxDocument);
        Assert.Equal("3526 1012 3456 7800 0195 5500 1000 0000 3810 0000 0381", row.AccessKey);
    }

    [Fact]
    public async Task BuildRows_WithoutTaxDocumentLeavesNumberAndKeyEmpty()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100m, date: Oct10);
        remittance.TaxDocumentNumber = null;
        await db.SaveChangesAsync();

        var row = (await Service(db).BuildRowsAsync(Request())).Single();

        Assert.Equal(remittance.InvoiceNumber, row.Document);
        Assert.Equal("", row.TaxDocument);
        Assert.Equal("", row.AccessKey);
    }

    [Fact]
    public async Task BuildRows_ReturnsAlwaysFollowTheRemittanceWithTheirOwnColumnsAndRunningBalance()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m, date: Oct10, number: "000000123");
        var second = await seed.AddReturnAsync(remittance, 20_000m, InvoiceStatus.Confirmed, date: Oct12, number: "000000502");
        var first = await seed.AddReturnAsync(remittance, 10_000m, InvoiceStatus.Pending, date: Oct11, number: "000000501");
        first.InvoiceNumber = "DE000026";
        first.TaxDocumentSeries = "1";
        first.ChaveNFe = ReturnKey;
        first.NfeStatus = NfeStatus.Authorized;
        await db.SaveChangesAsync();
        var a = await seed.AddShipmentAsync(4_000m, date: Oct11, code: "RM000001");
        var b = await seed.AddShipmentAsync(6_000m, date: Oct11, code: "RM000002");
        await Reconcile(db, first, a);
        await Reconcile(db, first, b);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(
            new[] { StorageRemittanceRowKind.Remittance, StorageRemittanceRowKind.Return, StorageRemittanceRowKind.Return },
            rows.Select(r => r.Kind));
        var r1 = rows[1];
        Assert.Equal("DE000026", r1.Document);
        Assert.Equal("000000501-1", r1.TaxDocument);
        Assert.Equal("11/10/2026", r1.InvoiceDate);
        Assert.Equal("Pendente", r1.Status);
        Assert.Equal("Autorizada", r1.NfeStatus);
        Assert.Equal("3526 1098 7654 3200 0110 5500 1000 0007 9010 0000 7901", r1.AccessKey);
        Assert.Equal("Retorno - romaneios: RM000001 (4.000,000 KG), RM000002 (6.000,000 KG)", r1.Note);
        Assert.Equal("10.000,000", r1.ReturnedText);
        Assert.Equal("90.000,000", r1.PendingReturnText);
        var r2 = rows[2];
        Assert.Equal(second.InvoiceNumber, r2.Document);
        Assert.Equal("000000502", r2.TaxDocument);
        Assert.Equal("Confirmado", r2.Status);
        Assert.Equal("Retorno - sem romaneio conciliado", r2.Note);
        Assert.Equal("20.000,000", r2.ReturnedText);
        Assert.Equal("70.000,000", r2.PendingReturnText);
        Assert.All(rows.Skip(1), r =>
        {
            Assert.Equal("SOJA - KG", r.Group);
            Assert.Equal("", r.Warehouse);
            Assert.Equal("", r.Contract);
            Assert.Equal("", r.RemittedText);
            Assert.Equal("", r.ShippedWithoutReturnText);
            Assert.Equal(0m, r.RemittedQuantity + r.ReturnedQuantity + r.PendingReturnQuantity + r.ShippedWithoutReturnQuantity);
            Assert.Equal(0, r.RemittanceCount);
        });
    }

    [Fact]
    public async Task BuildRows_DetailedAddsTheLoadingTransactionsAfterTheReturns()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m, date: Oct10, number: "000000123");
        var ret = await seed.AddReturnAsync(remittance, 10_000m, InvoiceStatus.Pending, date: Oct11, number: "000000501");
        ret.InvoiceNumber = "DE000026";
        await db.SaveChangesAsync();
        await seed.AddShipmentAsync(20_000m, date: Oct12, code: "RM000002");
        var loaded = await seed.AddShipmentAsync(10_000m, date: Oct11, code: "RM000001");
        await Reconcile(db, ret, loaded);

        var request = Request();
        request.Detailed = true;
        var rows = await Service(db).BuildRowsAsync(request);

        Assert.Equal(
            new[]
            {
                StorageRemittanceRowKind.Remittance, StorageRemittanceRowKind.Return,
                StorageRemittanceRowKind.Transaction, StorageRemittanceRowKind.Transaction,
            },
            rows.Select(r => r.Kind));
        Assert.Equal(
            new[]
            {
                "Romaneio RM000001 de 11/10/2026 - placa ABC1D23 - 10.000,000 KG - retorno DE000026",
                "Romaneio RM000002 de 12/10/2026 - placa ABC1D23 - 20.000,000 KG - sem retorno",
            },
            rows.Skip(2).Select(r => r.Detail));
        Assert.All(rows.Take(2), r => Assert.Equal("", r.Detail));
        Assert.All(rows.Skip(2), r =>
        {
            Assert.Equal("", r.Document);
            Assert.Equal("", r.AccessKey);
            Assert.Equal("", r.ReturnedText);
            Assert.Equal(0m, r.RemittedQuantity + r.ReturnedQuantity + r.PendingReturnQuantity + r.ShippedWithoutReturnQuantity);
            Assert.Equal(0, r.RemittanceCount);
        });
    }

    [Fact]
    public async Task BuildRows_WithoutDetailedHasNoLoadingTransactions()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100m, date: Oct10);
        await seed.AddReturnAsync(remittance, 10m, InvoiceStatus.Confirmed);
        await seed.AddShipmentAsync(10m);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { StorageRemittanceRowKind.Remittance, StorageRemittanceRowKind.Return }, rows.Select(r => r.Kind));
    }

    /// <summary>
    /// O relatório reescreve as regras de StorageDepositRules (o Reports não enxerga a Application). Este teste
    /// garante que, remessa a remessa, os quatro números são os da seção "Remessa para depósito" da liberação.
    /// </summary>
    [Fact]
    public async Task BuildRows_MatchesTheReleaseScreenForEveryRemittance()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);

        // A: Standard, retorno parcial, um romaneio reconciliado, um sem retorno, um de conferência.
        var a = await seed.AddRemittanceAsync(100_000m, date: Oct10, number: "000000001");
        var aReturn = await seed.AddReturnAsync(a, 30_000m, InvoiceStatus.Confirmed);
        await seed.AddReturnAsync(a, 5_000m, InvoiceStatus.Cancelled);
        var aLoaded = await seed.AddShipmentAsync(30_000m);
        await seed.AddShipmentAsync(12_000m);
        await seed.AddShipmentAsync(4_000m, origin: TransactionCode.WarehouseReconciliation);
        await Reconcile(db, aReturn, aLoaded);

        // B: transferência (Saída 7), totalmente retornada.
        var transfer = await seed.AddReleaseAsync(ReleaseOrigin.OwnershipTransfer);
        var b = await seed.AddRemittanceAsync(40_000m, release: transfer, date: Oct10, number: "000000002");
        await seed.AddReturnAsync(b, 40_000m, InvoiceStatus.Confirmed);
        await seed.AddShipmentAsync(40_000m, StorageTransactionType.SalesShipment, release: transfer);
        await seed.AddShipmentAsync(9_000m, release: transfer);

        // C: remessa editada para duas linhas, sem retorno.
        var other = await seed.AddReleaseAsync();
        var c = await seed.AddRemittanceAsync(60_000m, release: other, date: Oct10, number: "000000003");
        db.Context.SalesInvoicesItems.Add(new SalesInvoiceItem
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = c.Key, ItemCode = StorageDepositSeed.Item, UnitOfMeasureCode = "KG",
            Quantity = 40_000m, UsageCode = seed.RemittanceUsageCode,
        });
        await db.SaveChangesAsync();

        var rows = await Service(db).BuildRowsAsync(Request());
        var screen = new ShipmentReleasesStorageRemittanceSummaryService(db, TestConfiguration.Erp("STANDALONE"));

        foreach (var (release, number) in new[] { (seed.Release, "000000001"), (transfer, "000000002"), (other, "000000003") })
        {
            var expected = await screen.ExecuteAsync(release.Key);
            var row = Remittances(rows).Single(r => r.TaxDocument == number);
            Assert.Equal(expected.RemittedQuantity, row.RemittedQuantity);
            Assert.Equal(expected.ReturnedQuantity, row.ReturnedQuantity);
            Assert.Equal(expected.PendingReturnQuantity, row.PendingReturnQuantity);
            Assert.Equal(expected.ShippedWithoutReturnQuantity, row.ShippedWithoutReturnQuantity);
        }
    }

    [Fact]
    public async Task Execute_DescribesEveryFilter()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        await seed.AddRemittanceAsync(100m, date: Oct10);
        var recorder = new RecordingFastReportService();

        var request = Request();
        request.BranchCode = StorageDepositSeed.Branch;
        request.ItemCode = StorageDepositSeed.Item;
        request.CardCode = StorageDepositSeed.Warehouse;
        request.ReturnSituation = StorageRemittanceSituation.Open;
        request.Detailed = true;
        await new StorageRemittancesByPeriodReportService(db, recorder).ExecuteAsync(request);

        Assert.Equal("StorageRemittancesByPeriod.frx", recorder.LastReportName);
        Assert.Equal(
            "Data da remessa: 01/10/2026 a 31/10/2026 | Filial: MTZ | Produto: SOJA | Situação: em aberto | " +
            "Armazém: ARM001 | Detalhado",
            recorder.LastParameters!["pFilters"]);
        Assert.Equal(true, recorder.LastParameters![FastReportService.SingleUomParameter]);
    }

    [Theory]
    [InlineData(StorageRemittanceSituation.All, "Situação: todas")]
    [InlineData(StorageRemittanceSituation.Returned, "Situação: totalmente retornada")]
    public async Task Execute_DescribesTheSituation(StorageRemittanceSituation situation, string expected)
    {
        var db = TestDb.CreateUnitOfWork();
        var recorder = new RecordingFastReportService();
        var request = Request();
        request.ReturnSituation = situation;

        await new StorageRemittancesByPeriodReportService(db, recorder).ExecuteAsync(request);

        Assert.Equal($"Data da remessa: 01/10/2026 a 31/10/2026 | {expected}", recorder.LastParameters!["pFilters"]);
    }

    private static IEnumerable<StorageRemittanceRowDto> Remittances(IEnumerable<StorageRemittanceRowDto> rows) =>
        rows.Where(r => r.Kind == StorageRemittanceRowKind.Remittance);

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
