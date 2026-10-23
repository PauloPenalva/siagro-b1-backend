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

/// <summary>ZIP de DACTEs: um PDF por CT-e autorizado; o resto é ignorado.</summary>
public class DacteBatchServiceTests
{
    static DacteBatchServiceTests()
    {
        FastReport.Utils.Config.WebMode = true;
    }

    private static DacteBatchService Service(UnitOfWork db)
    {
        var contentRoot = Path.Combine(AppContext.BaseDirectory, "ReportsContentRoot");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CompanyLogoPath"] = "wwwroot/images/logo.png" })
            .Build();
        var environment = new TestWebHostEnvironment(contentRoot);
        var header = new ReportHeaderService(environment, configuration, NullLogger<ReportHeaderService>.Instance, FakeCompanyContext.Unresolved());
        return new DacteBatchService(db, new DacteReportService(db, environment, header));
    }

    [Fact]
    public async Task Dacte_batch_zip_contains_one_pdf_per_authorized()
    {
        var db = TestDb.CreateUnitOfWork();
        var proc = DacteReportTests.AuthorizedProc();
        var accessKey = System.Text.RegularExpressions.Regex.Match(proc, "<chCTe>(\\d{44})</chCTe>").Groups[1].Value;
        var authorized = new TransportDocument { BranchCode = "01", Status = CteStatus.Authorized, AccessKey = accessKey };
        var confirmed = new TransportDocument { BranchCode = "01", Status = CteStatus.Confirmed };
        db.Context.TransportDocuments.AddRange(authorized, confirmed);
        db.Context.TransportDocumentXmls.Add(new TransportDocumentXml { TransportDocumentKey = authorized.Key, Kind = CteXmlKind.Authorized, Xml = proc });
        await db.SaveChangesAsync();

        var bytes = await Service(db).BuildAsync([authorized.Key, confirmed.Key]);

        using var zip = new ZipArchive(new MemoryStream(bytes));
        var entry = Assert.Single(zip.Entries);
        Assert.Equal($"{accessKey}-dacte.pdf", entry.FullName);
        var head = new byte[4];
        using var stream = entry.Open();
        await stream.ReadExactlyAsync(head);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(head));
    }

    [Fact]
    public async Task Dacte_batch_with_nothing_authorized_is_refused()
    {
        var db = TestDb.CreateUnitOfWork();
        var confirmed = new TransportDocument { BranchCode = "01", Status = CteStatus.Confirmed };
        db.Context.TransportDocuments.Add(confirmed);
        await db.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(db).BuildAsync([confirmed.Key]));

        Assert.Equal("Nenhum CT-e autorizado entre os selecionados.", e.Message);
    }

    [Fact]
    public async Task Dacte_batch_over_200_is_refused()
    {
        var keys = Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()).ToList();

        await Assert.ThrowsAsync<DefaultException>(() => Service(TestDb.CreateUnitOfWork()).BuildAsync(keys));
    }
}
