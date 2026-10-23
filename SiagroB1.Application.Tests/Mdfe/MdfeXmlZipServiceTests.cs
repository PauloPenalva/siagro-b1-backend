using System.IO.Compression;
using SiagroB1.Application.Services.Mdfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Mdfe;

/// <summary>ZIP de XML do MDF-e: mdfeProc de quem foi autorizado (inclusive encerrado/cancelado) + eventos, por chave.</summary>
public class MdfeXmlZipServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();
    private int _seq = 1;

    private MdfeXmlZipService Service(string erp = "STANDALONE") => new(_db, MdfeTestSupport.Config(erp));

    private async Task<TransportManifest> SeedAsync(MdfeStatus status, params (MdfeXmlKind Kind, string Xml)[] xmls)
    {
        var m = new TransportManifest { BranchCode = "01", Status = status, AccessKey = _seq++.ToString().PadLeft(44, '1') };
        _db.Context.TransportManifests.Add(m);
        foreach (var (kind, xml) in xmls)
            _db.Context.TransportManifestXmls.Add(new TransportManifestXml { ManifestKey = m.Key, Kind = kind, Xml = xml });
        await _db.SaveChangesAsync();
        return m;
    }

    [Fact]
    public async Task Zip_contains_authorized_and_event_xmls_named_by_key()
    {
        var authorized = await SeedAsync(MdfeStatus.Authorized, (MdfeXmlKind.Authorized, "<mdfeProc>1</mdfeProc>"),
            (MdfeXmlKind.DriverInclusion, "<procEventoMDFe>d</procEventoMDFe>"), (MdfeXmlKind.Signed, "<MDFe>s</MDFe>"));
        var closed = await SeedAsync(MdfeStatus.Closed, (MdfeXmlKind.Authorized, "<mdfeProc>2</mdfeProc>"),
            (MdfeXmlKind.Closing, "<procEventoMDFe>e</procEventoMDFe>"));
        var cancelled = await SeedAsync(MdfeStatus.Cancelled, (MdfeXmlKind.Authorized, "<mdfeProc>3</mdfeProc>"),
            (MdfeXmlKind.Cancellation, "<procEventoMDFe>c</procEventoMDFe>"));

        var bytes = await Service().BuildAsync([authorized.Key, closed.Key, cancelled.Key]);

        using var zip = new ZipArchive(new MemoryStream(bytes));
        Assert.Equal(
            new[]
            {
                $"MDFe{authorized.AccessKey}.xml", $"MDFe{authorized.AccessKey}-condutor.xml",
                $"MDFe{closed.AccessKey}.xml", $"MDFe{closed.AccessKey}-enc.xml",
                $"MDFe{cancelled.AccessKey}.xml", $"MDFe{cancelled.AccessKey}-canc.xml",
            }.Order().ToList(),
            zip.Entries.Select(x => x.FullName).Order().ToList());
        using var reader = new StreamReader(zip.GetEntry($"MDFe{cancelled.AccessKey}-canc.xml")!.Open());
        Assert.Equal("<procEventoMDFe>c</procEventoMDFe>", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task Zip_skips_unauthorized()
    {
        var authorized = await SeedAsync(MdfeStatus.Authorized, (MdfeXmlKind.Authorized, "<mdfeProc>1</mdfeProc>"));
        var draft = await SeedAsync(MdfeStatus.Draft);
        var rejected = await SeedAsync(MdfeStatus.Rejected, (MdfeXmlKind.Signed, "<MDFe>s</MDFe>"));

        var bytes = await Service().BuildAsync([authorized.Key, draft.Key, rejected.Key]);

        using var zip = new ZipArchive(new MemoryStream(bytes));
        Assert.Equal($"MDFe{authorized.AccessKey}.xml", Assert.Single(zip.Entries).FullName);
    }

    [Fact]
    public async Task Zip_nothing_authorized_is_refused()
    {
        var draft = await SeedAsync(MdfeStatus.Draft);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service().BuildAsync([draft.Key]));

        Assert.Equal("Nenhum MDF-e autorizado entre os selecionados.", e.Message);
    }

    [Fact]
    public async Task Zip_over_50_is_refused()
    {
        var keys = Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()).ToList();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service().BuildAsync(keys));

        Assert.Equal("Selecione no máximo 50 MDF-e por vez.", e.Message);
    }

    [Fact]
    public async Task Zip_outside_standalone_is_refused()
    {
        await Assert.ThrowsAsync<DefaultException>(() => Service("SAPB1").BuildAsync([Guid.NewGuid()]));
    }
}
