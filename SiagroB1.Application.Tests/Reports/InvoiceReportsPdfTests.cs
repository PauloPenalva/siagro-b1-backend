using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Tests.Support;
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
