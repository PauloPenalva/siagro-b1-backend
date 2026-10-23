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

        await WriteSampleAsync(report, pdf, "DAMDFE_SAMPLE_PDF");
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
        Assert.Equal("ENCERRADO - SEM VALOR FISCAL", watermark.Text);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(await service.RenderAsync(key), 0, 4));
    }

    [Fact]
    public async Task Damdfe_cancelled_shows_cancelled_stamp()
    {
        var db = TestDb.CreateUnitOfWork();
        var (key, _) = await SeedAsync(db, MdfeStatus.Cancelled);
        var service = Service(db);

        using var report = service.Prepare(await service.LoadAsync(key));

        Assert.Equal("CANCELADO - SEM VALOR FISCAL", report.Pages.OfType<ReportPage>().First().Watermark.Text);
    }

    [Theory]
    [InlineData(false, false, false, null)]
    [InlineData(true, false, false, "SEM VALOR FISCAL")]
    [InlineData(false, true, false, "CANCELADO")]
    [InlineData(false, false, true, "ENCERRADO")]
    [InlineData(true, true, false, "CANCELADO - SEM VALOR FISCAL")]
    [InlineData(true, false, true, "ENCERRADO - SEM VALOR FISCAL")]
    public void Stamp_keeps_homologation_warning(bool homologation, bool cancelled, bool closed, string? expected)
    {
        var dto = new SiagroB1.Reports.Dtos.DamdfeDto { Homologation = homologation, Cancelled = cancelled, Closed = closed };
        Assert.Equal(expected, DamdfeReportService.Stamp(dto));
    }

    [Fact]
    public async Task Damdfe_with_many_ctes_overflows_to_extra_pages_without_dropping_keys()
    {
        var keys = Enumerable.Range(1, 60)
            .Select(i => SiagroB1.Fiscal.Cte.CteAccessKey.Build("35", new DateTime(2026, 10, 22), "12345678000195", 231, i, 1, (10000000 + i).ToString()))
            .ToList();
        var input = MdfeTestData.Input() with
        {
            UnloadingCities =
            [
                new MdfeUnloadingCity("3550308", "São Paulo", keys.Take(30).ToList()),
                new MdfeUnloadingCity("3509502", "Campinas", keys.Skip(30).ToList()),
            ],
            Totals = new MdfeTotals(60, 3600000m, "01", 1800000m),
            Ciot = new MdfeCiot("123456789012", "12345678000195"),
            AdditionalInfo = string.Join(" ", Enumerable.Repeat("OBSERVACAO LONGA DO MDF-E PARA TESTAR O CRESCIMENTO DO QUADRO.", 30)) + " FIM-DAS-OBS",
        };
        var db = TestDb.CreateUnitOfWork();
        var (key, _) = await SeedAsync(db, MdfeStatus.Authorized, input);
        var service = Service(db);

        var dto = await service.LoadAsync(key);
        using var report = service.Prepare(dto);

        Assert.True(report.PreparedPages.Count > 1, $"páginas: {report.PreparedPages.Count}");
        var text = Texts(report);
        foreach (var cte in keys)
            Assert.Contains(DacteXmlReader.FormatKey(cte), text);
        Assert.Contains("FIM-DAS-OBS", text);
        Assert.Contains($"1/{report.PreparedPages.Count}", text);
        Assert.Contains($"{report.PreparedPages.Count}/{report.PreparedPages.Count}", text);

        await WriteSampleAsync(report, await service.RenderAsync(key), "DAMDFE_SAMPLE_MANY_PDF");
    }

    /// <summary>Com a variável de ambiente apontando um .pdf, grava o PDF e um PNG por página para conferência visual.</summary>
    private static async Task WriteSampleAsync(Report report, byte[] pdf, string variable)
    {
        if (Environment.GetEnvironmentVariable(variable) is not { Length: > 0 } sample)
            return;
        await File.WriteAllBytesAsync(sample, pdf);
        using var png = new FastReport.Export.Image.ImageExport
        {
            ImageFormat = FastReport.Export.Image.ImageExportFormat.Png, Resolution = 110, SeparateFiles = true,
        };
        report.Export(png, Path.ChangeExtension(sample, ".png"));
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
    public static string AuthorizedProc(MdfeIssueInput? custom = null)
    {
        using var certificate = CertificateLoader.Load(TestCertificates.CreatePfx("12345678000195"), TestCertificates.Password);
        var settings = new MdfeServiceSettings(NfeEnvironment.Homologation, "SP", certificate, MdfeZeusConfiguration.DefaultSchemasDirectory);
        var input = MdfeTestData.Input() with
        {
            Ciot = new MdfeCiot("123456789012", "12345678000195"),
            TollVoucher = new MdfeTollVoucher("11222333000181", "20261023", 85.50m),
            AdditionalInfo = "CARGA A GRANEL",
        };
        input = custom ?? input;
        var signed = MdfeSigner.SignAndValidate(MdfeXmlBuilder.Build(input), settings);
        return MdfeProc.Compose(signed.Xml, FakeMdfeSefazClient.Authorized(signed.AccessKey).ProtocolXml!);
    }

    private static async Task<(Guid Key, string AccessKey)> SeedAsync(UnitOfWork db, MdfeStatus status, MdfeIssueInput? input = null)
    {
        var proc = AuthorizedProc(input);
        var accessKey = System.Text.RegularExpressions.Regex.Match(proc, @"<chMDFe>(\d{44})</chMDFe>").Groups[1].Value;
        var manifest = new TransportManifest { BranchCode = "01", Status = status, AccessKey = accessKey };
        db.Context.TransportManifests.Add(manifest);
        db.Context.Set<TransportManifestXml>().Add(new TransportManifestXml { ManifestKey = manifest.Key, Kind = MdfeXmlKind.Authorized, Xml = proc });
        await db.SaveChangesAsync();
        return (manifest.Key, accessKey);
    }
}
