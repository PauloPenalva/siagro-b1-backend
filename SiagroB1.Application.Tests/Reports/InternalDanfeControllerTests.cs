using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Fiscal.Tests.Support;
using SiagroB1.Infra;
using SiagroB1.Infra.Companies;
using SiagroB1.Reports.Controllers;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>Rota interna do DANFE para o job de e-mail: fora de /reports (o Gateway não a expõe) e protegida por chave.</summary>
public class InternalDanfeControllerTests
{
    private const string ApiKey = "chave-interna-de-teste";

    private static readonly CompanyInfo Ceagui = new("0002", "CEAGUI", "Server=.;Database=CEAGUI;Integrated Security=True");

    private readonly FakeCompanyContext _company = FakeCompanyContext.Unresolved();

    private InternalDanfeController Controller(UnitOfWork db, string? configuredKey, string? key, string? company)
    {
        var contentRoot = Path.Combine(AppContext.BaseDirectory, "ReportsContentRoot");
        var environment = new TestWebHostEnvironment(contentRoot);
        var configuration = TestConfiguration.With(("Reports:InternalApiKey", configuredKey));
        var header = new ReportHeaderService(environment, configuration, NullLogger<ReportHeaderService>.Instance, _company);

        var services = new ServiceCollection()
            .AddSingleton(new DanfeReportService(db, environment, header))
            .BuildServiceProvider();

        var http = new DefaultHttpContext { RequestServices = services };
        if (key is not null) http.Request.Headers["X-Internal-Key"] = key;
        if (company is not null) http.Request.Headers["X-Company"] = company;

        return new InternalDanfeController(new FakeCompanyCatalog(Ceagui), _company, configuration)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    [Theory]
    [InlineData(ApiKey, "errada")]
    [InlineData(ApiKey, null)]
    [InlineData("", "")]
    [InlineData(null, "")]
    public async Task Wrong_key_returns_404(string? configured, string? sent)
    {
        var result = await Controller(TestDb.CreateUnitOfWork(), configured, sent, "0002").Get("sales", Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result);
        Assert.False(_company.IsResolved);
    }

    [Fact]
    public async Task Unknown_company_returns_404()
    {
        var result = await Controller(TestDb.CreateUnitOfWork(), ApiKey, ApiKey, "9999").Get("sales", Guid.NewGuid());

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Valid_key_sets_company_and_returns_pdf()
    {
        var db = TestDb.CreateUnitOfWork();
        using var certificate = CertificateLoader.Load(TestCertificates.CreatePfx(), TestCertificates.Password);
        var settings = new NfeServiceSettings(NfeEnvironment.Homologation, "SP", certificate, NfeServiceSettings.DefaultSchemasDirectory);
        var signed = NfeSigner.BuildSignAndValidate(NfeTestData.Input(), settings);
        var invoiceKey = Guid.NewGuid();
        db.Context.SalesInvoiceNfeXmls.Add(new SalesInvoiceNfeXml
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = invoiceKey, Kind = NfeXmlKind.Authorized, CreatedAt = DateTime.Now,
            Xml = NfeProcComposer.Compose(signed.Xml, FakeNfeSefazClient.Authorized(signed.AccessKey).ProtocolXml!),
        });
        await db.SaveChangesAsync();

        var result = await Controller(db, ApiKey, ApiKey, "0002").Get("sales", invoiceKey);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(file.FileContents, 0, 4));
        Assert.Equal("0002", _company.Current?.Code);
    }
}
