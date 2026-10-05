using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Fiscal.Tests.Support;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>O DANFE sai do procNFe gravado, pelo layout da Zeus copiado sem alteração.</summary>
public class DanfeReportServiceTests
{
    [Fact]
    public async Task Authorized_nfe_renders_a_pdf()
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

        var contentRoot = Path.Combine(AppContext.BaseDirectory, "ReportsContentRoot");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CompanyLogoPath"] = "wwwroot/images/logo.png" })
            .Build();
        var environment = new TestWebHostEnvironment(contentRoot);
        var header = new ReportHeaderService(environment, configuration, NullLogger<ReportHeaderService>.Instance);

        var (pdf, fileName) = await new DanfeReportService(db, environment, header).GeneratePdfAsync(invoiceKey);

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.Equal($"{signed.AccessKey}-danfe.pdf", fileName);
    }

    [Fact]
    public async Task Without_authorized_nfe_it_is_not_found()
    {
        var db = TestDb.CreateUnitOfWork();
        var contentRoot = Path.Combine(AppContext.BaseDirectory, "ReportsContentRoot");
        var environment = new TestWebHostEnvironment(contentRoot);
        var header = new ReportHeaderService(environment, new ConfigurationBuilder().Build(), NullLogger<ReportHeaderService>.Instance);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new DanfeReportService(db, environment, header).GeneratePdfAsync(Guid.NewGuid()));
    }
}
