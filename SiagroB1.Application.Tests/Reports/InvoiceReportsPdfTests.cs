using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.InvoiceReportSeed;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>
/// Gera o PDF de ponta a ponta com o template real, nos dois modos. Pega fonte de dados com
/// nome divergente, coluna que o FastReport não converte e objeto fiscal mal nomeado.
/// Grava o PDF em %TEMP%/siagro-invoice-reports para conferência visual.
/// </summary>
public class InvoiceReportsPdfTests : IDisposable
{
    private readonly string _contentRoot;

    public InvoiceReportsPdfTests()
    {
        global::FastReport.Utils.RegisteredObjects.AddConnection(typeof(global::FastReport.Data.MsSqlDataConnection));
        global::FastReport.Utils.Config.WebMode = true;

        _contentRoot = Path.Combine(Path.GetTempPath(), "siagro-invoice-pdf", Guid.NewGuid().ToString("N"));
        var templates = Path.Combine(_contentRoot, "Reports", "Templates");
        Directory.CreateDirectory(templates);
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "ReportTemplates"), "*.frx"))
            File.Copy(file, Path.Combine(templates, Path.GetFileName(file)));

        var images = Path.Combine(_contentRoot, "wwwroot", "images");
        Directory.CreateDirectory(images);
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "ReportsContentRoot", "wwwroot", "images", "logo.png"),
            Path.Combine(images, "logo.png"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_contentRoot))
            Directory.Delete(_contentRoot, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("STANDALONE")]
    [InlineData("SAPB1")]
    public async Task SalesInvoicesByPeriod_ProducesAPdf(string erp)
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = Sale("1");
        invoice.TaxDocumentNumber = "000123";
        invoice.TaxDocumentSeries = "1";
        db.Context.SalesInvoices.Add(invoice);
        db.Context.SalesInvoices.Add(Sale("2")); // sem NF: coluna vazia
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var configuration = Configuration(erp);
        var pdf = await new SalesInvoicesByPeriodReportService(db, FastReport(configuration), configuration)
            .ExecuteAsync(new SalesInvoicesByPeriodRequest { FromDate = Jul01, ToDate = Jul31 });

        Keep("SalesInvoicesByPeriod", erp, pdf);
    }

    [Fact]
    public async Task SalesInvoicesByPeriod_EmptyResultStillProducesAPdf()
    {
        var db = TestDb.CreateUnitOfWork();
        var configuration = Configuration("STANDALONE");

        var pdf = await new SalesInvoicesByPeriodReportService(db, FastReport(configuration), configuration)
            .ExecuteAsync(new SalesInvoicesByPeriodRequest { FromDate = Jul01, ToDate = Jul31 });

        Assert.NotEmpty(pdf);
    }

    [Theory]
    [InlineData("STANDALONE")]
    [InlineData("SAPB1")]
    public async Task PurchaseInvoicesByPeriod_ProducesAPdf(string erp)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseInvoices.Add(Purchase("1"));
        db.Context.PurchaseInvoices.Add(Purchase("2", issuer: DocumentIssuerType.Own, type: PurchaseInvoiceType.Return));
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var configuration = Configuration(erp);
        var pdf = await new PurchaseInvoicesByPeriodReportService(db, FastReport(configuration), configuration)
            .ExecuteAsync(new PurchaseInvoicesByPeriodRequest { FromDate = Jul01, ToDate = Jul31 });

        Keep("PurchaseInvoicesByPeriod", erp, pdf);
    }

    [Theory]
    [InlineData("STANDALONE")]
    [InlineData("SAPB1")]
    public async Task SalesInvoiceItems_ProducesAPdf(string erp)
    {
        var db = TestDb.CreateUnitOfWork();
        var taxed = SaleItem("10001", "SOJA");
        taxed.Cfop = "5101";
        taxed.UsageCode = 21;
        taxed.UsageName = "VENDA DE PRODUÇÃO";
        taxed.IcmsValue = 12m;
        taxed.PisValue = 1.65m;
        taxed.CofinsValue = 7.6m;
        var invoice = Sale("1", items: [taxed, SaleItem("10002", "MILHO", uom: "KG")]);
        invoice.TaxDocumentNumber = "000123";
        invoice.TaxDocumentSeries = "1";
        db.Context.SalesInvoices.Add(invoice);
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var configuration = Configuration(erp);
        var pdf = await new SalesInvoiceItemsReportService(db, FastReport(configuration), configuration)
            .ExecuteAsync(new SalesInvoiceItemsRequest { FromDate = Jul01, ToDate = Jul31 });

        Keep("SalesInvoiceItems", erp, pdf);
    }

    [Theory]
    [InlineData("STANDALONE")]
    [InlineData("SAPB1")]
    public async Task PurchaseInvoiceItems_ProducesAPdf(string erp)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseInvoices.Add(Purchase("1", items: [PurchaseItem(), PurchaseItem(null, "SOJA DO XML", uom: null)]));
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var configuration = Configuration(erp);
        var pdf = await new PurchaseInvoiceItemsReportService(db, FastReport(configuration), configuration)
            .ExecuteAsync(new PurchaseInvoiceItemsRequest { FromDate = Jul01, ToDate = Jul31 });

        Keep("PurchaseInvoiceItems", erp, pdf);
    }

    [Theory]
    [InlineData("STANDALONE")]
    [InlineData("SAPB1")]
    public async Task SalesReturns_ProducesAPdf(string erp)
    {
        var db = TestDb.CreateUnitOfWork();
        var originalItem = SaleItem();
        db.Context.SalesInvoices.Add(Sale("100", new DateTime(2026, 6, 20), items: [originalItem]));
        var own = SaleItem(quantity: 2m);
        own.SalesInvoiceItemOriginKey = originalItem.Key;
        db.Context.SalesInvoices.Add(Sale("101", type: SalesInvoiceType.Return, items: [own]));
        db.Context.PurchaseInvoices.Add(Purchase("900", type: PurchaseInvoiceType.Return)); // sem vínculo
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var configuration = Configuration(erp);
        var pdf = await new SalesReturnsReportService(db, FastReport(configuration), configuration)
            .ExecuteAsync(new SalesReturnsRequest { FromDate = Jul01, ToDate = Jul31 });

        Keep("SalesReturns", erp, pdf);
    }

    // Valores grandes: exercitam a largura dos campos numéricos (ver InvoiceReportsLayoutTests).
    [Fact]
    public async Task SalesInvoicesByPeriod_LargeValuesProduceAPdf()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(Sale("1", items: [SaleItem(quantity: 5_000_000m, unitPrice: 1.5m)]));
        db.Context.SalesInvoices.Add(Sale("2", items: [SaleItem(quantity: 3_000_000m, unitPrice: 1.5m)]));
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var configuration = Configuration("STANDALONE");
        var pdf = await new SalesInvoicesByPeriodReportService(db, FastReport(configuration), configuration)
            .ExecuteAsync(new SalesInvoicesByPeriodRequest { FromDate = Jul01, ToDate = Jul31 });

        Keep("SalesInvoicesByPeriod-large", "STANDALONE", pdf);
    }

    [Fact]
    public async Task PurchaseInvoicesByPeriod_LargeValuesProduceAPdf()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseInvoices.Add(Purchase("1", items: [PurchaseItem(quantity: 5_000_000m, unitPrice: 1.5m)]));
        db.Context.PurchaseInvoices.Add(Purchase("2", items: [PurchaseItem(quantity: 3_000_000m, unitPrice: 1.5m)]));
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var configuration = Configuration("STANDALONE");
        var pdf = await new PurchaseInvoicesByPeriodReportService(db, FastReport(configuration), configuration)
            .ExecuteAsync(new PurchaseInvoicesByPeriodRequest { FromDate = Jul01, ToDate = Jul31 });

        Keep("PurchaseInvoicesByPeriod-large", "STANDALONE", pdf);
    }

    [Fact]
    public async Task SalesInvoiceItems_LargeValuesProduceAPdf()
    {
        var db = TestDb.CreateUnitOfWork();
        SalesInvoiceItem Big(decimal quantity)
        {
            var item = SaleItem(quantity: quantity, unitPrice: 1.25m);
            item.IcmsValue = 10_800m;
            item.PisValue = 99_999.99m;
            item.CofinsValue = 999_999.99m;
            return item;
        }
        db.Context.SalesInvoices.Add(Sale("1", items: [Big(3_000_000m), Big(3_000_000m), Big(2_225_167m)]));
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var configuration = Configuration("STANDALONE");
        var pdf = await new SalesInvoiceItemsReportService(db, FastReport(configuration), configuration)
            .ExecuteAsync(new SalesInvoiceItemsRequest { FromDate = Jul01, ToDate = Jul31 });

        Keep("SalesInvoiceItems-large", "STANDALONE", pdf);
    }

    [Fact]
    public async Task PurchaseInvoiceItems_LargeValuesProduceAPdf()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseInvoices.Add(Purchase("1", items:
        [
            PurchaseItem(quantity: 3_000_000m, unitPrice: 1.25m),
            PurchaseItem(quantity: 3_000_000m, unitPrice: 1.25m),
            PurchaseItem(quantity: 2_225_167m, unitPrice: 1.25m),
        ]));
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var configuration = Configuration("STANDALONE");
        var pdf = await new PurchaseInvoiceItemsReportService(db, FastReport(configuration), configuration)
            .ExecuteAsync(new PurchaseInvoiceItemsRequest { FromDate = Jul01, ToDate = Jul31 });

        Keep("PurchaseInvoiceItems-large", "STANDALONE", pdf);
    }

    [Fact]
    public async Task SalesReturns_LargeValuesProduceAPdf()
    {
        var db = TestDb.CreateUnitOfWork();
        var originalItem = SaleItem(quantity: 9_000_000m);
        db.Context.SalesInvoices.Add(Sale("100", new DateTime(2026, 6, 20), items: [originalItem]));
        var own = SaleItem(quantity: 8_225_167m, unitPrice: 1.25m);
        own.SalesInvoiceItemOriginKey = originalItem.Key;
        db.Context.SalesInvoices.Add(Sale("101", type: SalesInvoiceType.Return, items: [own]));
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var configuration = Configuration("STANDALONE");
        var pdf = await new SalesReturnsReportService(db, FastReport(configuration), configuration)
            .ExecuteAsync(new SalesReturnsRequest { FromDate = Jul01, ToDate = Jul31 });

        Keep("SalesReturns-large", "STANDALONE", pdf);
    }

    [Theory]
    [InlineData("STANDALONE")]
    [InlineData("SAPB1")]
    public async Task FunruralWithholding_ProducesAPdf(string erp)
    {
        var db = TestDb.CreateUnitOfWork();
        var bought = PurchaseItem();
        bought.FunruralBase = 1000m;
        bought.FunruralInssRate = 1.2m;
        bought.FunruralInssValue = 12m;
        bought.FunruralRatRate = 0.1m;
        bought.FunruralRatValue = 1m;
        bought.FunruralSenarRate = 0.2m;
        bought.FunruralSenarValue = 2m;
        db.Context.PurchaseInvoices.Add(Purchase("1", items: [bought]));

        var returned = PurchaseItem();
        returned.PurchaseInvoiceItemOriginKey = bought.Key;
        returned.FunruralBase = 400m;
        returned.FunruralInssValue = 4.8m;
        returned.FunruralRatValue = 0.4m;
        returned.FunruralSenarValue = 0.8m;
        var ret = Purchase("DEV", new DateTime(2026, 8, 5), type: PurchaseInvoiceType.Return, issuer: DocumentIssuerType.Own, items: [returned]);
        ret.IsNfeReturn = true;
        db.Context.PurchaseInvoices.Add(ret);
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var configuration = Configuration(erp);
        var pdf = await new FunruralWithholdingReportService(db, FastReport(configuration), configuration)
            .ExecuteAsync(new FunruralWithholdingRequest { FromDate = Jul01, ToDate = Jul31 });

        Keep("FunruralWithholding", erp, pdf);
    }

    [Theory]
    [InlineData("STANDALONE")]
    [InlineData("SAPB1")]
    public async Task RoyaltyPending_ProducesAPdf(string erp)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.ItemComplements.Add(new ItemComplement { ItemCode = "SOJA", ControlsRoyalties = true });
        db.Context.StorageTransactions.Add(new StorageTransaction
        {
            Key = Guid.NewGuid(), Code = "R1", CardCode = "P0001", CardName = "Produtor", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", WarehouseCode = "ARM01", TransactionType = StorageTransactionType.Receipt,
            TransactionStatus = StorageTransactionsStatus.Confirmed, TransactionDate = Jul01,
            NetWeight = 31_234.567m, RoyaltySituation = RoyaltySituation.Tested, RoyaltyTestCode = "T-1",
            RoyaltyOriginCode = "P0001", RoyaltyOriginName = "Produtor",
        });
        StorageTransaction Extra(string code, decimal net) => db.Context.StorageTransactions.Add(new StorageTransaction
        {
            Key = Guid.NewGuid(), Code = code, CardCode = "P0001", CardName = "Produtor", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", WarehouseCode = "ARM01", TransactionType = StorageTransactionType.Receipt,
            TransactionStatus = StorageTransactionsStatus.Confirmed, TransactionDate = Jul01,
            NetWeight = net, RoyaltySituation = RoyaltySituation.Tested, RoyaltyOriginCode = "P0001",
            RoyaltyOriginName = "Produtor",
        }).Entity;
        void WriteOff(string its, StorageTransaction st, decimal weight, DateTime date) =>
            db.Context.RoyaltyWriteOffs.Add(new RoyaltyWriteOff
            {
                ItsId = its, TaxId = "12345678909", Name = "Produtor de Teste", Situation = RoyaltySituation.Tested,
                WriteOffDate = date,
                Lines = [new RoyaltyWriteOffLine { StorageTransactionKey = st.Key, Weight = weight }],
            });
        var partial = db.Context.StorageTransactions.Local.Single(x => x.Code == "R1");   // parcial, 1 baixa
        var settled = Extra("R2", 20_000m);                                               // total, 2 baixas
        WriteOff("ITS-0001", partial, 10_000.5m, Jul01);
        WriteOff("ITS-0002", settled, 12_000m, Jul01);
        WriteOff("ITS-0003", settled, 8_000m, Jul01.AddDays(1));
        Extra("R3", 5_000m);                                                              // pendente, sem baixa
        await db.SaveChangesAsync();
        var configuration = Configuration(erp);

        var pdf = await new RoyaltyPendingReportService(db, FastReport(configuration), configuration)
            .ExecuteAsync(new RoyaltyPendingRequest { FromDate = Jul01, ToDate = Jul31, Scope = "All" });

        Keep("RoyaltyPending", erp, pdf);
        if (erp == "STANDALONE")
        {
            var target = Environment.GetEnvironmentVariable("ROYALTY_SCOPE_PDF");
            if (!string.IsNullOrEmpty(target)) File.WriteAllBytes(target, pdf);
        }
    }

    private FastReportService FastReport(IConfiguration configuration)
    {
        var env = new TestWebHostEnvironment(_contentRoot);
        return new FastReportService(env, configuration,
            new ReportHeaderService(env, configuration, new TestLogger<ReportHeaderService>()));
    }

    private static IConfiguration Configuration(string erp) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Erp"] = erp,
                ["CompanyName"] = "ACME AGRO LTDA",
                ["CompanyLogoPath"] = "wwwroot/images/logo.png",
            })
            .Build();

    private static void Keep(string report, string erp, byte[] pdf)
    {
        Assert.NotEmpty(pdf);
        var folder = Path.Combine(Path.GetTempPath(), "siagro-invoice-reports");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, $"{report}-{erp}.pdf"), pdf);
    }
}
