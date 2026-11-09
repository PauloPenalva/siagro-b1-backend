using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Fiscal.Tests.Support;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>Exportação para o contador: ZIP com XMLs, eventos e DANFEs das NF-e do período.</summary>
public class NfeExportServiceTests
{
    private static readonly DateTime Day = new(2026, 10, 15, 10, 0, 0);

    private static NfeExportService Service(UnitOfWork db)
    {
        var environment = new TestWebHostEnvironment(Path.Combine(AppContext.BaseDirectory, "ReportsContentRoot"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CompanyLogoPath"] = "wwwroot/images/logo.png" })
            .Build();
        var header = new ReportHeaderService(environment, configuration, NullLogger<ReportHeaderService>.Instance, FakeCompanyContext.Unresolved());
        return new NfeExportService(db, new DanfeReportService(db, environment, header));
    }

    private static (string AccessKey, string ProcXml) AuthorizedProc()
    {
        using var certificate = CertificateLoader.Load(TestCertificates.CreatePfx(), TestCertificates.Password);
        var settings = new NfeServiceSettings(NfeEnvironment.Homologation, "SP", certificate, NfeServiceSettings.DefaultSchemasDirectory);
        var signed = NfeSigner.BuildSignAndValidate(NfeTestData.Input(), settings);
        return (signed.AccessKey, NfeProcComposer.Compose(signed.Xml, FakeNfeSefazClient.Authorized(signed.AccessKey).ProtocolXml!));
    }

    private static SalesInvoice AddSales(UnitOfWork db, string accessKey, string xml, NfeStatus status, DateTime authorizedAt, string branch = "01")
    {
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(), BranchCode = branch, CardCode = "C001", CardName = "Cliente Teste", ChaveNFe = accessKey,
            TaxDocumentNumber = "123", TaxDocumentSeries = "1", NfeStatus = status, NfeAuthorizedAt = authorizedAt,
        };
        db.Context.SalesInvoices.Add(invoice);
        db.Context.SalesInvoiceNfeXmls.Add(new SalesInvoiceNfeXml
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = invoice.Key, Kind = NfeXmlKind.Authorized, Xml = xml, CreatedAt = authorizedAt,
        });
        return invoice;
    }

    private static NfeExportRequest Request(bool danfe = true, bool received = true, string? branch = null) => new()
    {
        StartDate = new DateTime(2026, 10, 1), EndDate = new DateTime(2026, 10, 31),
        BranchCode = branch, IncludeDanfe = danfe, IncludeReceived = received,
    };

    private static ZipArchive Open(byte[] bytes) => new(new MemoryStream(bytes));

    private static string Read(ZipArchive zip, string name)
    {
        using var reader = new StreamReader(zip.GetEntry(name)!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [Fact]
    public async Task Authorized_sales_nfe_goes_with_xml_danfe_and_summary()
    {
        var db = TestDb.CreateUnitOfWork();
        var (accessKey, xml) = AuthorizedProc();
        AddSales(db, accessKey, xml, NfeStatus.Authorized, Day);
        await db.SaveChangesAsync();

        var (bytes, fileName) = await Service(db).BuildAsync(Request());

        Assert.Equal("NFe-20261001-20261031.zip", fileName);
        using var zip = Open(bytes);
        Assert.Equal(xml, Read(zip, $"Saidas/XML/{accessKey}-procNFe.xml"));
        using (var pdf = zip.GetEntry($"Saidas/DANFE/{accessKey}-danfe.pdf")!.Open())
        {
            var head = new byte[4];
            await pdf.ReadExactlyAsync(head);
            Assert.Equal("%PDF", Encoding.ASCII.GetString(head));
        }

        var summary = Read(zip, "resumo.csv");
        Assert.Contains(accessKey, summary);
        Assert.Contains("Autorizada", summary);
    }

    [Fact]
    public async Task Cancelled_nfe_includes_cancellation_event_and_correction_letters()
    {
        var db = TestDb.CreateUnitOfWork();
        var (accessKey, xml) = AuthorizedProc();
        var invoice = AddSales(db, accessKey, xml, NfeStatus.Cancelled, Day);
        db.Context.SalesInvoiceNfeXmls.Add(new SalesInvoiceNfeXml
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = invoice.Key, Kind = NfeXmlKind.CancellationEvent, Xml = "<procEventoNFe/>", CreatedAt = Day,
        });
        db.Context.SalesInvoiceNfeCorrections.Add(new SalesInvoiceNfeCorrection
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = invoice.Key, Sequence = 1, Text = "corrige", ProcEventXml = "<cce/>",
            CreatedAt = Day, CreatedBy = "test", StatusCode = 135, Reason = "ok",
        });
        await db.SaveChangesAsync();

        var (bytes, _) = await Service(db).BuildAsync(Request(danfe: false));

        using var zip = Open(bytes);
        Assert.Equal("<procEventoNFe/>", Read(zip, $"Saidas/Cancelamentos/{accessKey}-cancelamento.xml"));
        Assert.Equal("<cce/>", Read(zip, $"Saidas/CCe/{accessKey}-cce-1.xml"));
        Assert.Null(zip.GetEntry($"Saidas/DANFE/{accessKey}-danfe.pdf"));
        Assert.Contains("Cancelada", Read(zip, "resumo.csv"));
    }

    [Fact]
    public async Task Out_of_period_and_other_branch_are_left_out()
    {
        var db = TestDb.CreateUnitOfWork();
        var (accessKey, xml) = AuthorizedProc();
        AddSales(db, accessKey, xml, NfeStatus.Authorized, Day);
        AddSales(db, "1".PadLeft(44, '1'), xml, NfeStatus.Authorized, new DateTime(2026, 11, 1, 0, 0, 0));
        AddSales(db, "2".PadLeft(44, '2'), xml, NfeStatus.Authorized, Day, branch: "02");
        await db.SaveChangesAsync();

        var (bytes, fileName) = await Service(db).BuildAsync(Request(danfe: false, branch: "01"));

        Assert.Equal("NFe-20261001-20261031-01.zip", fileName);
        using var zip = Open(bytes);
        var xmls = zip.Entries.Where(e => e.FullName.StartsWith("Saidas/XML/")).Select(e => e.FullName).ToList();
        Assert.Equal([$"Saidas/XML/{accessKey}-procNFe.xml"], xmls);
    }

    [Fact]
    public async Task Last_day_of_period_is_included()
    {
        var db = TestDb.CreateUnitOfWork();
        var (accessKey, xml) = AuthorizedProc();
        AddSales(db, accessKey, xml, NfeStatus.Authorized, new DateTime(2026, 10, 31, 23, 59, 0));
        await db.SaveChangesAsync();

        var (bytes, _) = await Service(db).BuildAsync(Request(danfe: false));

        using var zip = Open(bytes);
        Assert.NotNull(zip.GetEntry($"Saidas/XML/{accessKey}-procNFe.xml"));
    }

    [Fact]
    public async Task Received_third_party_xml_goes_to_its_folder_when_asked()
    {
        var db = TestDb.CreateUnitOfWork();
        var accessKey = "3".PadLeft(44, '3');
        db.Context.ReceivedDfes.Add(new ReceivedDfe
        {
            BranchCode = "01", AccessKey = accessKey, IssuerName = "Fornecedor", IssueDate = new DateTimeOffset(Day),
            TotalValue = 10m, Xml = "<nfeProc/>", CreatedAt = DateTimeOffset.Now,
        });
        await db.SaveChangesAsync();

        var (bytes, _) = await Service(db).BuildAsync(Request());
        using (var zip = Open(bytes))
            Assert.Equal("<nfeProc/>", Read(zip, $"Terceiros/XML/{accessKey}.xml"));

        await Assert.ThrowsAsync<DefaultException>(() => Service(db).BuildAsync(Request(received: false)));
    }

    [Fact]
    public async Task Empty_period_is_refused()
    {
        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(TestDb.CreateUnitOfWork()).BuildAsync(Request()));

        Assert.Equal(NfeExportService.NothingFound, e.Message);
    }

    [Fact]
    public async Task Inverted_or_too_long_period_is_refused()
    {
        var service = Service(TestDb.CreateUnitOfWork());

        await Assert.ThrowsAsync<DefaultException>(() => service.BuildAsync(new NfeExportRequest
        {
            StartDate = new DateTime(2026, 10, 31), EndDate = new DateTime(2026, 10, 1),
        }));
        await Assert.ThrowsAsync<DefaultException>(() => service.BuildAsync(new NfeExportRequest
        {
            StartDate = new DateTime(2025, 1, 1), EndDate = new DateTime(2026, 10, 1),
        }));
    }
}
