using System.IO.Compression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>ZIP de DAMDFEs: um PDF por MDF-e autorizado/encerrado/cancelado; o resto é ignorado.</summary>
public class DamdfeBatchTests
{
    static DamdfeBatchTests()
    {
        FastReport.Utils.Config.WebMode = true;
    }

    private static DamdfeBatchService Service(UnitOfWork db)
    {
        var contentRoot = Path.Combine(AppContext.BaseDirectory, "ReportsContentRoot");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CompanyLogoPath"] = "wwwroot/images/logo.png" })
            .Build();
        var environment = new TestWebHostEnvironment(contentRoot);
        var header = new ReportHeaderService(environment, configuration, NullLogger<ReportHeaderService>.Instance, FakeCompanyContext.Unresolved());
        return new DamdfeBatchService(db, new DamdfeReportService(db, environment, header));
    }

    [Fact]
    public async Task Damdfe_batch_returns_one_pdf_per_key()
    {
        var db = TestDb.CreateUnitOfWork();
        var proc = DamdfeReportTests.AuthorizedProc();
        var accessKey = System.Text.RegularExpressions.Regex.Match(proc, @"<chMDFe>(\d{44})</chMDFe>").Groups[1].Value;
        var authorized = new TransportManifest { BranchCode = "01", Status = MdfeStatus.Authorized, AccessKey = accessKey };
        var draft = new TransportManifest { BranchCode = "01", Status = MdfeStatus.Draft };
        db.Context.TransportManifests.AddRange(authorized, draft);
        db.Context.TransportManifestXmls.Add(new TransportManifestXml { ManifestKey = authorized.Key, Kind = MdfeXmlKind.Authorized, Xml = proc });
        await db.SaveChangesAsync();

        var bytes = await Service(db).BuildAsync([authorized.Key, draft.Key]);

        using var zip = new ZipArchive(new MemoryStream(bytes));
        var entry = Assert.Single(zip.Entries);
        Assert.Equal($"{accessKey}-damdfe.pdf", entry.FullName);
        var head = new byte[4];
        using var stream = entry.Open();
        await stream.ReadExactlyAsync(head);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(head));
    }

    [Fact]
    public async Task Damdfe_batch_with_nothing_authorized_is_refused()
    {
        var db = TestDb.CreateUnitOfWork();
        var draft = new TransportManifest { BranchCode = "01", Status = MdfeStatus.Draft };
        db.Context.TransportManifests.Add(draft);
        await db.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(db).BuildAsync([draft.Key]));

        Assert.Equal("Nenhum MDF-e autorizado entre os selecionados.", e.Message);
    }

    [Fact]
    public async Task Damdfe_batch_over_50_is_refused()
    {
        var keys = Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()).ToList();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(TestDb.CreateUnitOfWork()).BuildAsync(keys));

        Assert.Equal("Selecione no máximo 50 MDF-e por vez.", e.Message);
    }
}
