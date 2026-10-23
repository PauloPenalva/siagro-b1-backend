using FastReport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Mdfe;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Fiscal.Tests.Support;
using SiagroB1.Infra;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>O DAMDFE sai do mdfeProc autorizado gravado (não da entidade), pelo Damdfe.frx próprio.</summary>
public class DamdfeReportTests
{
    static DamdfeReportTests()
    {
        FastReport.Utils.Config.WebMode = true;
    }

    [Fact]
    public async Task Damdfe_renders_pdf_from_authorized_xml()
    {
        var db = TestDb.CreateUnitOfWork();
        var (key, accessKey) = await SeedAsync(db, MdfeStatus.Authorized);
        var service = Service(db);

        var pdf = await service.RenderAsync(key);

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        var dto = await service.LoadAsync(key);
        using var report = service.Prepare(dto);
        var text = Texts(report);
        Assert.Contains(DacteXmlReader.FormatKey(accessKey), text);
        Assert.Contains("CEAGUI CEREAIS LTDA", text);
        Assert.Contains("RODOVIÁRIO", text);
        Assert.Contains(FakeMdfeSefazClient.AuthorizationProtocol, text);
        Assert.Contains("ABC1D23", text);
        Assert.Contains("XYZ9A87", text);
        Assert.Contains("JOAO MOTORISTA", text);
        Assert.Contains("123456789012", text); // CIOT
        Assert.Contains("20261023", text); // vale-pedágio
        Assert.Contains("São Paulo", text);
        Assert.Contains(DacteXmlReader.FormatKey(MdfeTestData.CteKey2), text);
        Assert.Contains("60.000,0000", text); // peso
        Assert.Equal("SEM VALOR FISCAL", report.Pages.OfType<ReportPage>().First().Watermark.Text);
        Assert.StartsWith("https://", dto.QrCode);

        if (Environment.GetEnvironmentVariable("DAMDFE_SAMPLE_PDF") is { Length: > 0 } sample)
        {
            await File.WriteAllBytesAsync(sample, pdf);
            using var png = new FastReport.Export.Image.ImageExport { ImageFormat = FastReport.Export.Image.ImageExportFormat.Png, Resolution = 110 };
            report.Export(png, Path.ChangeExtension(sample, ".png"));
        }
    }

    [Fact]
    public async Task Damdfe_closed_shows_closed_stamp()
    {
        var db = TestDb.CreateUnitOfWork();
        var (key, _) = await SeedAsync(db, MdfeStatus.Closed);
        var service = Service(db);

        var dto = await service.LoadAsync(key);
        using var report = service.Prepare(dto);

        var watermark = report.Pages.OfType<ReportPage>().First().Watermark;
        Assert.True(watermark.Enabled);
        Assert.Contains("ENCERRADO", watermark.Text);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(await service.RenderAsync(key), 0, 4));
    }

    [Fact]
    public async Task Damdfe_cancelled_shows_cancelled_stamp()
    {
        var db = TestDb.CreateUnitOfWork();
        var (key, _) = await SeedAsync(db, MdfeStatus.Cancelled);
        var service = Service(db);

        using var report = service.Prepare(await service.LoadAsync(key));

        Assert.Contains("CANCELADO", report.Pages.OfType<ReportPage>().First().Watermark.Text);
    }

    [Fact]
    public async Task Damdfe_for_unauthorized_is_refused()
    {
        var db = TestDb.CreateUnitOfWork();
        var draft = new TransportManifest { BranchCode = "01", Status = MdfeStatus.Confirmed };
        db.Context.TransportManifests.Add(draft);
        await db.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(db).RenderAsync(draft.Key));
        Assert.Contains("autorizado", e.Message);
        await Assert.ThrowsAsync<DefaultException>(() => Service(db).RenderAsync(Guid.NewGuid()));
    }

    private static string Texts(Report report) => string.Join("\n",
        Enumerable.Range(0, report.PreparedPages.Count)
            .SelectMany(i => report.PreparedPages.GetPage(i).AllObjects.OfType<TextObject>())
            .Select(t => t.Text));

    private static DamdfeReportService Service(UnitOfWork db)
    {
        var contentRoot = Path.Combine(AppContext.BaseDirectory, "ReportsContentRoot");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CompanyLogoPath"] = "wwwroot/images/logo.png" })
            .Build();
        var environment = new TestWebHostEnvironment(contentRoot);
        var header = new ReportHeaderService(environment, configuration, NullLogger<ReportHeaderService>.Instance, FakeCompanyContext.Unresolved());
        return new DamdfeReportService(db, environment, header);
    }

    /// <summary>mdfeProc = MDF-e montado e assinado pelo Fiscal (com CIOT e vale-pedágio) + protMDFe simulado.</summary>
    public static string AuthorizedProc()
    {
        using var certificate = CertificateLoader.Load(TestCertificates.CreatePfx("12345678000195"), TestCertificates.Password);
        var settings = new MdfeServiceSettings(NfeEnvironment.Homologation, "SP", certificate, MdfeZeusConfiguration.DefaultSchemasDirectory);
        var input = MdfeTestData.Input() with
        {
            Ciot = new MdfeCiot("123456789012", "12345678000195"),
            TollVoucher = new MdfeTollVoucher("11222333000181", "20261023", 85.50m),
            AdditionalInfo = "CARGA A GRANEL",
        };
        var signed = MdfeSigner.SignAndValidate(MdfeXmlBuilder.Build(input), settings);
        return MdfeProc.Compose(signed.Xml, FakeMdfeSefazClient.Authorized(signed.AccessKey).ProtocolXml!);
    }

    private static async Task<(Guid Key, string AccessKey)> SeedAsync(UnitOfWork db, MdfeStatus status)
    {
        var proc = AuthorizedProc();
        var accessKey = System.Text.RegularExpressions.Regex.Match(proc, @"<chMDFe>(\d{44})</chMDFe>").Groups[1].Value;
        var manifest = new TransportManifest { BranchCode = "01", Status = status, AccessKey = accessKey };
        db.Context.TransportManifests.Add(manifest);
        db.Context.Set<TransportManifestXml>().Add(new TransportManifestXml { ManifestKey = manifest.Key, Kind = MdfeXmlKind.Authorized, Xml = proc });
        await db.SaveChangesAsync();
        return (manifest.Key, accessKey);
    }
}
