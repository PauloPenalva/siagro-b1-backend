using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.LogisticsReportSeed;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>
/// Gera o PDF de ponta a ponta com o template real, nos dois modos (o relatório não tem coluna
/// fiscal; os dois modos provam que nada depende de tabela do SAP). Pega fonte de dados com nome
/// divergente e coluna que o FastReport não converte. Grava em %TEMP%/siagro-logistics-reports.
/// </summary>
public class LogisticsReportsPdfTests : IDisposable
{
    private readonly string _contentRoot;

    public LogisticsReportsPdfTests()
    {
        global::FastReport.Utils.RegisteredObjects.AddConnection(typeof(global::FastReport.Data.MsSqlDataConnection));
        global::FastReport.Utils.Config.WebMode = true;

        _contentRoot = Path.Combine(Path.GetTempPath(), "siagro-logistics-pdf", Guid.NewGuid().ToString("N"));
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
    public async Task ShipmentLoadsByPeriod_ProducesAPdf(string erp)
    {
        var db = TestDb.CreateUnitOfWork();
        var several = Load("CG000001");
        several.InvoicedQuantity = 20_000m;
        several.FreightPrice = 4_500.50m;
        db.Context.ShipmentLoads.Add(several);
        db.Context.SalesInvoices.Add(LoadInvoice(several, "000000101", "C001", "COOPERATIVA CENTRAL"));
        db.Context.SalesInvoices.Add(LoadInvoice(several, "000000102", "C002", "AGRO NORTE"));
        db.Context.ShipmentLoads.Add(Load("CG000002", status: ShipmentLoadStatus.Planned, total: 0m,
            plannedCardCode: "C009", plannedCardName: "FAZENDA BOA VISTA"));
        db.Context.ShipmentLoads.Add(Load("CG000003", itemCode: "20001", itemName: "MILHO", uom: "TN", total: 30m));
        await Save(db);

        var pdf = await new ShipmentLoadsByPeriodReportService(db, FastReport(Configuration(erp)))
            .ExecuteAsync(new ShipmentLoadsByPeriodRequest { FromDate = Jul01, ToDate = Jul31 });

        Keep("ShipmentLoadsByPeriod", erp, pdf);
    }

    [Fact]
    public async Task ShipmentLoadsByPeriod_EmptyResultStillProducesAPdf()
    {
        var db = TestDb.CreateUnitOfWork();

        var pdf = await new ShipmentLoadsByPeriodReportService(db, FastReport(Configuration("STANDALONE")))
            .ExecuteAsync(new ShipmentLoadsByPeriodRequest { FromDate = Jul01, ToDate = Jul31 });

        Assert.NotEmpty(pdf);
    }

    // Valores grandes: exercitam a largura dos campos numéricos (ver LogisticsReportsLayoutTests).
    [Fact]
    public async Task ShipmentLoadsByPeriod_LargeValuesProduceAPdf()
    {
        var db = TestDb.CreateUnitOfWork();
        foreach (var code in new[] { "CG000001", "CG000002" })
        {
            var load = Load(code, total: 99_999_999.999m);
            load.InvoicedQuantity = 88_888_888.888m;
            load.ReturnedToWarehouseQuantity = 9_999_999.999m;
            load.TransshippedQuantity = 1_111_111.112m;
            load.DischargedQuantity = 99_999_999.999m;
            load.FreightPrice = 9_999_999.99m;
            db.Context.ShipmentLoads.Add(load);
            db.Context.SalesInvoices.Add(LoadInvoice(load, code, "C001", "COOPERATIVA AGROINDUSTRIAL DOS PRODUTORES DO NORTE"));
        }
        await Save(db);

        var pdf = await new ShipmentLoadsByPeriodReportService(db, FastReport(Configuration("STANDALONE")))
            .ExecuteAsync(new ShipmentLoadsByPeriodRequest { FromDate = Jul01, ToDate = Jul31 });

        Keep("ShipmentLoadsByPeriod-large", "STANDALONE", pdf);
    }

    // pSingleUom de ponta a ponta: o serviço calcula, o FastReportService real recebe e o PDF sai.
    // (O texto do PDF não é extraível, então a conferência do que fica visível é visual — ver os
    // PDFs gravados — e o ApplySingleUom é coberto em FastReportServiceUomTests.)
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ShipmentLoadsByPeriod_PassesTheSingleUomFlagToTheRealService(bool withTonnes, bool singleUom)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.ShipmentLoads.Add(Load("CG000001"));
        if (withTonnes)
            db.Context.ShipmentLoads.Add(Load("CG000002", uom: "TN", total: 30m));
        await Save(db);
        var spy = new SpyFastReportService(FastReport(Configuration("STANDALONE")));

        var pdf = await new ShipmentLoadsByPeriodReportService(db, spy)
            .ExecuteAsync(new ShipmentLoadsByPeriodRequest { FromDate = Jul01, ToDate = Jul31 });

        Assert.NotEmpty(pdf);
        Assert.Equal(singleUom, spy.LastParameters![FastReportService.SingleUomParameter]);
    }

    private sealed class SpyFastReportService(IFastReportService inner) : IFastReportService
    {
        public Dictionary<string, object>? LastParameters { get; private set; }

        public Task<byte[]> GeneratePdfAsync(string reportName, Dictionary<string, object> parameters)
        {
            LastParameters = parameters;
            return inner.GeneratePdfAsync(reportName, parameters);
        }

        public Task<byte[]> GeneratePdfAsync<T>(string reportName, ICollection<T> data, string dataSourceName,
            string refName, Dictionary<string, object> parameters)
        {
            LastParameters = parameters;
            return inner.GeneratePdfAsync(reportName, data, dataSourceName, refName, parameters);
        }
    }

    private FastReportService FastReport(IConfiguration configuration)
    {
        var env = new TestWebHostEnvironment(_contentRoot);
        return new FastReportService(env, configuration,
            new ReportHeaderService(env, configuration, new TestLogger<ReportHeaderService>()));
    }

    [Theory]
    [InlineData("STANDALONE")]
    [InlineData("SAPB1")]
    public async Task SalesShipmentReleasesByPeriod_ProducesAPdf(string erp)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.LogisticRegions.Add(Region());
        var soja = SalesContract("CV000001");
        var milho = SalesContract("CV000002", itemCode: "20001", itemName: "MILHO", agentName: null);
        db.Context.SalesContracts.AddRange(soja, milho);
        db.Context.SalesShipmentReleases.Add(SalesRelease(soja, released: 1_000m, shipped: 400m));
        db.Context.SalesShipmentReleases.Add(SalesRelease(soja, released: 1_000m, shipped: 1_200m, status: ReleaseStatus.Completed));
        db.Context.SalesShipmentReleases.Add(SalesRelease(milho, deliveryLocationName: null));
        await Save(db);

        var pdf = await new SalesShipmentReleasesByPeriodReportService(db, FastReport(Configuration(erp)))
            .ExecuteAsync(new SalesShipmentReleasesByPeriodRequest { FromDate = Jul01, ToDate = Jul31 });

        Keep("SalesShipmentReleasesByPeriod", erp, pdf);
    }

    [Fact]
    public async Task SalesShipmentReleasesByPeriod_EmptyResultStillProducesAPdf()
    {
        var db = TestDb.CreateUnitOfWork();

        var pdf = await new SalesShipmentReleasesByPeriodReportService(db, FastReport(Configuration("STANDALONE")))
            .ExecuteAsync(new SalesShipmentReleasesByPeriodRequest { FromDate = Jul01, ToDate = Jul31 });

        Assert.NotEmpty(pdf);
    }

    [Fact]
    public async Task SalesShipmentReleasesByPeriod_LargeValuesProduceAPdf()
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = SalesContract("CV2026000123",
            cardName: "COOPERATIVA AGROINDUSTRIAL DOS PRODUTORES DO NORTE DO PARANÁ");
        db.Context.SalesContracts.Add(contract);
        db.Context.SalesShipmentReleases.Add(SalesRelease(contract, released: 99_999_999.999m, shipped: 11_111_111.111m,
            deliveryLocationName: "TERMINAL PORTUÁRIO DE PARANAGUÁ - CORREDOR DE EXPORTAÇÃO"));
        db.Context.SalesShipmentReleases.Add(SalesRelease(contract, released: 99_999_999.999m, shipped: 11_111_111.111m));
        await Save(db);

        var pdf = await new SalesShipmentReleasesByPeriodReportService(db, FastReport(Configuration("STANDALONE")))
            .ExecuteAsync(new SalesShipmentReleasesByPeriodRequest { FromDate = Jul01, ToDate = Jul31 });

        Keep("SalesShipmentReleasesByPeriod-large", "STANDALONE", pdf);
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

    private static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }

    private static void Keep(string report, string erp, byte[] pdf)
    {
        Assert.NotEmpty(pdf);
        var folder = Path.Combine(Path.GetTempPath(), "siagro-logistics-reports");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, $"{report}-{erp}.pdf"), pdf);
    }
}
