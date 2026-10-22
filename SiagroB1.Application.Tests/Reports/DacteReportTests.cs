using FastReport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Cte;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Fiscal.Tests.Support;
using SiagroB1.Infra;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>O DACTE sai do cteProc autorizado gravado (não da entidade), pelo Dacte.frx próprio.</summary>
public class DacteReportTests
{
    static DacteReportTests()
    {
        FastReport.Utils.Config.WebMode = true;
    }

    [Fact]
    public async Task Dacte_renders_pdf_from_authorized_xml()
    {
        var db = TestDb.CreateUnitOfWork();
        var (key, accessKey) = await SeedAsync(db, CteStatus.Authorized);
        var service = Service(db);

        var pdf = await service.RenderAsync(key);

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        var dto = await service.LoadAsync(key);
        using var report = service.Prepare(dto);
        var text = Texts(report);
        Assert.Contains(DacteXmlReader.FormatKey(accessKey), text);
        Assert.Contains("COSTA PRODUTOR RURAL LTDA", text);
        Assert.Contains("ARMAZEM DESTINO LTDA", text);
        Assert.Contains("SOJA EM GRAOS", text);
        Assert.Contains("FRETE PESO", text);
        Assert.Contains(FakeCteSefazClient.AuthorizationProtocol, text);
        Assert.Contains("12345678", text); // RNTRC
        Assert.Equal("SEM VALOR FISCAL", report.Pages.OfType<ReportPage>().First().Watermark.Text);
        Assert.StartsWith("https://", dto.QrCode);

        if (Environment.GetEnvironmentVariable("DACTE_SAMPLE_PDF") is { Length: > 0 } sample)
        {
            await File.WriteAllBytesAsync(sample, pdf);
            using var png = new FastReport.Export.Image.ImageExport { ImageFormat = FastReport.Export.Image.ImageExportFormat.Png, Resolution = 110 };
            report.Export(png, Path.ChangeExtension(sample, ".png"));
        }
    }

    [Fact]
    public async Task Dacte_for_cancelled_shows_cancelled_stamp()
    {
        var db = TestDb.CreateUnitOfWork();
        var (key, _) = await SeedAsync(db, CteStatus.Cancelled);
        var service = Service(db);

        var dto = await service.LoadAsync(key);
        using var report = service.Prepare(dto);

        Assert.True(dto.Cancelled);
        var watermark = report.Pages.OfType<ReportPage>().First().Watermark;
        Assert.True(watermark.Enabled);
        Assert.Contains("CANCELADO", watermark.Text);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(await service.RenderAsync(key), 0, 4));
    }

    [Fact]
    public async Task Dacte_for_unauthorized_document_is_refused()
    {
        var db = TestDb.CreateUnitOfWork();
        var draft = new TransportDocument { BranchCode = "01", Status = CteStatus.Confirmed };
        db.Context.TransportDocuments.Add(draft);
        await db.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(db).RenderAsync(draft.Key));
        Assert.Contains("autorizado", e.Message);
        await Assert.ThrowsAsync<DefaultException>(() => Service(db).RenderAsync(Guid.NewGuid()));
    }

    private static string Texts(Report report) => string.Join("\n",
        Enumerable.Range(0, report.PreparedPages.Count)
            .SelectMany(i => report.PreparedPages.GetPage(i).AllObjects.OfType<TextObject>())
            .Select(t => t.Text));

    private static DacteReportService Service(UnitOfWork db)
    {
        var contentRoot = Path.Combine(AppContext.BaseDirectory, "ReportsContentRoot");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CompanyLogoPath"] = "wwwroot/images/logo.png" })
            .Build();
        var environment = new TestWebHostEnvironment(contentRoot);
        var header = new ReportHeaderService(environment, configuration, NullLogger<ReportHeaderService>.Instance, FakeCompanyContext.Unresolved());
        return new DacteReportService(db, environment, header);
    }

    /// <summary>cteProc = CT-e montado e assinado pelo Fiscal + protCTe simulado.</summary>
    public static string AuthorizedProc()
    {
        using var certificate = CertificateLoader.Load(TestCertificates.CreatePfx("12345678000195"), TestCertificates.Password);
        var settings = new CteServiceSettings(NfeEnvironment.Homologation, "SP", certificate, CteServiceSettings.DefaultSchemasDirectory);
        var signed = CteSigner.SignAndValidate(CteXmlBuilder.Build(CteTestData.Input()), settings);
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?><cteProc versao=\"4.00\" xmlns=\"http://www.portalfiscal.inf.br/cte\">"
               + Body(signed.Xml) + Body(FakeCteSefazClient.Authorized(signed.AccessKey).ProtocolXml!) + "</cteProc>";
    }

    private static string Body(string xml)
    {
        var text = xml.Trim();
        return text.StartsWith("<?xml", StringComparison.Ordinal) ? text[(text.IndexOf("?>", StringComparison.Ordinal) + 2)..].Trim() : text;
    }

    private static async Task<(Guid Key, string AccessKey)> SeedAsync(UnitOfWork db, CteStatus status)
    {
        var proc = AuthorizedProc();
        var accessKey = System.Text.RegularExpressions.Regex.Match(proc, "<chCTe>(\\d{44})</chCTe>").Groups[1].Value;
        var doc = new TransportDocument
        {
            BranchCode = "01", Status = status, AccessKey = accessKey,
            CancellationProtocol = status == CteStatus.Cancelled ? "135260000000001" : null,
        };
        db.Context.TransportDocuments.Add(doc);
        db.Context.TransportDocumentXmls.Add(new TransportDocumentXml { TransportDocumentKey = doc.Key, Kind = CteXmlKind.Authorized, Xml = proc });
        await db.SaveChangesAsync();
        return (doc.Key, accessKey);
    }
}
