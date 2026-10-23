using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Jobs;
using SiagroB1.Application.Services.Cte;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.Transport;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Certificates;

namespace SiagroB1.Application.Tests.Cte;

/// <summary>Lote de CT-e: um job por documento elegível, o job nunca relança e o ZIP de XML.</summary>
public class CteBatchServiceTests : CteEventsTestBase
{
    private readonly FakeBackgroundJobClient _jobs = new();

    private CteBatchService Batch(string? erp = "STANDALONE") =>
        new(Db, NfeTestSeed.Config(erp), _jobs, new FakeCompanyContext("0002"));

    private sealed class FakeAverba : IAverbaClient
    {
        public int SendCalls { get; private set; }
        public bool FailFirst { get; set; }

        public Task<AverbaLoginResult> LoginAsync(NfeEnvironment environment, string user, string password, string code, CancellationToken ct) =>
            Task.FromResult(new AverbaLoginResult(true, "tok", null));

        public Task<AverbaCteResult> SendCteAsync(NfeEnvironment environment, string bearer, string xml, CancellationToken ct)
        {
            SendCalls++;
            if (FailFirst && SendCalls == 1)
                throw new InvalidOperationException("AT&M fora do ar");
            return Task.FromResult(new AverbaCteResult(true, "PROT-" + SendCalls, "2026-10-23T09:00:00", [], null));
        }
    }

    private readonly FakeAverba _averba = new();

    private CteBatchJob Job()
    {
        var config = NfeTestSeed.Config();
        var transmit = new TransportDocumentsTransmitService(
            Db, config, Numbers, Contexts(), Sefaz, new NfeOptions(config), () => Clock);
        var endorse = new TransportDocumentsEndorseService(Db, config, Numbers, _averba);
        return new CteBatchJob(transmit, endorse, Db, NullLogger<CteBatchJob>.Instance);
    }

    private async Task AddEndorsementSettingsAsync()
    {
        Db.Context.BranchTransportSettings.Add(new BranchTransportSettings
        {
            BranchCode = "01", AverbaEnabled = true, AverbaUser = "u", AverbaCode = "c",
            AverbaEnvironment = NfeEnvironment.Homologation,
            AverbaPasswordCipher = CertificatePasswordCipher.FromBase64(NfeTestSeed.CertificateKey).Encrypt("segredo"),
        });
        await Db.SaveChangesAsync();
    }

    private async Task AddXmlAsync(Guid key, CteXmlKind kind, string xml)
    {
        Db.Context.TransportDocumentXmls.Add(new TransportDocumentXml { TransportDocumentKey = key, Kind = kind, Xml = xml });
        await Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Batch_enqueues_one_job_per_eligible_document()
    {
        var a = await SeedAsync(CteStatus.Confirmed, 101);
        var b = await SeedAsync(CteStatus.Rejected, 102);
        var c = await SeedAsync(CteStatus.Processing, 103);

        var count = await Batch().EnqueueTransmitAsync([a, b, c, a], "tester");

        Assert.Equal(3, count);
        Assert.Equal(new[] { a, b, c }.Order().ToList(), _jobs.EnqueuedOutboxKeys.Order().ToList());
        Assert.All(_jobs.EnqueuedCompanyCodes, code => Assert.Equal("0002", code));
        Assert.All(_jobs.Enqueued, job => Assert.Equal(typeof(CteBatchJob), job.Type));
    }

    [Fact]
    public async Task Batch_skips_ineligible_and_reports_count()
    {
        var confirmed = await SeedAsync(CteStatus.Confirmed, 101);
        var authorized = await SeedAsync(CteStatus.Authorized, 102);
        var endorsed = await SeedAsync(CteStatus.Authorized, 103);
        Db.Context.TransportDocuments.Single(x => x.Key == endorsed).EndorsementStatus = CteEndorsementStatus.Endorsed;
        await Db.SaveChangesAsync();
        var keys = new List<Guid> { confirmed, authorized, endorsed, Guid.NewGuid() };

        Assert.Equal(1, await Batch().EnqueueTransmitAsync(keys, "tester"));
        Assert.Equal([confirmed], _jobs.EnqueuedOutboxKeys);

        _jobs.Enqueued.Clear();
        Assert.Equal(1, await Batch().EnqueueEndorseAsync(keys, "tester"));
        Assert.Equal([authorized], _jobs.EnqueuedOutboxKeys);
    }

    [Fact]
    public async Task Batch_over_200_is_refused()
    {
        var keys = Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()).ToList();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Batch().EnqueueTransmitAsync(keys, "tester"));

        Assert.Equal("Selecione no máximo 200 CT-e por vez.", e.Message);
        await Assert.ThrowsAsync<DefaultException>(() => Batch().EnqueueEndorseAsync(keys, "tester"));
        Assert.Empty(_jobs.Enqueued);
    }

    [Fact]
    public async Task Batch_outside_standalone_is_refused()
    {
        await Assert.ThrowsAsync<DefaultException>(() => Batch("SAPB1").EnqueueTransmitAsync([Guid.NewGuid()], "tester"));
    }

    [Fact]
    public async Task Transmit_job_failure_is_recorded_without_throwing()
    {
        var draft = await SeedAsync(CteStatus.Draft, 101);

        await Job().TransmitAsync("0002", draft, "tester");

        var doc = await LoadAsync(draft);
        Assert.Equal(TransportDocumentsTransmitService.NotTransmittable, doc.StatusReason);
    }

    [Fact]
    public async Task Transmit_job_failure_does_not_clobber_an_authorized_document()
    {
        var authorized = await SeedAsync(CteStatus.Authorized, 101);

        await Job().TransmitAsync("0002", authorized, "tester");

        var doc = await LoadAsync(authorized);
        Assert.Equal(CteStatus.Authorized, doc.Status);
        Assert.Null(doc.StatusReason);
    }

    [Fact]
    public async Task Job_failure_on_one_does_not_throw_and_others_run()
    {
        var first = await SeedAsync(CteStatus.Authorized, 101);
        var second = await SeedAsync(CteStatus.Authorized, 102);
        await AddXmlAsync(first, CteXmlKind.Authorized, "<cteProc>1</cteProc>");
        await AddXmlAsync(second, CteXmlKind.Authorized, "<cteProc>2</cteProc>");
        await AddEndorsementSettingsAsync();
        _averba.FailFirst = true;
        var job = Job();

        await job.EndorseAsync("0002", first, "tester");
        await job.EndorseAsync("0002", second, "tester");

        var a = await LoadAsync(first);
        var b = await LoadAsync(second);
        Assert.Equal(CteEndorsementStatus.None, a.EndorsementStatus);
        Assert.Equal("AT&M fora do ar", a.EndorsementError);
        Assert.Equal(CteEndorsementStatus.Endorsed, b.EndorsementStatus);
        Assert.Null(b.EndorsementError);
        Assert.Equal(0, Numbers.LocksHeld);
    }

    [Fact]
    public async Task Endorse_job_records_business_refusal()
    {
        var key = await SeedAsync(CteStatus.Authorized, 101);
        await AddXmlAsync(key, CteXmlKind.Authorized, "<cteProc>1</cteProc>");

        await Job().EndorseAsync("0002", key, "tester");

        var doc = await LoadAsync(key);
        Assert.Equal(TransportDocumentsEndorseService.NotConfigured, doc.EndorsementError);
    }

    [Fact]
    public async Task Xml_zip_has_authorized_xml_and_cancellation_event_and_skips_the_rest()
    {
        var authorized = await SeedAsync(CteStatus.Authorized, 101);
        var cancelled = await SeedAsync(CteStatus.Cancelled, 102);
        var confirmed = await SeedAsync(CteStatus.Confirmed, 103);
        await AddXmlAsync(authorized, CteXmlKind.Authorized, "<cteProc>1</cteProc>");
        await AddXmlAsync(cancelled, CteXmlKind.Authorized, "<cteProc>2</cteProc>");
        await AddXmlAsync(cancelled, CteXmlKind.Cancellation, "<procEventoCTe>c</procEventoCTe>");
        var authorizedKey = (await LoadAsync(authorized)).AccessKey;
        var cancelledKey = (await LoadAsync(cancelled)).AccessKey;

        var bytes = await new CteXmlZipService(Db, NfeTestSeed.Config()).BuildAsync([authorized, cancelled, confirmed]);

        using var zip = new ZipArchive(new MemoryStream(bytes));
        var names = zip.Entries.Select(x => x.FullName).Order().ToList();
        Assert.Equal(
            new[] { $"CTe{authorizedKey}.xml", $"CTe{cancelledKey}.xml", $"CTe{cancelledKey}-canc.xml" }.Order().ToList(),
            names);
        using var reader = new StreamReader(zip.GetEntry($"CTe{cancelledKey}-canc.xml")!.Open());
        Assert.Equal("<procEventoCTe>c</procEventoCTe>", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task Xml_zip_with_nothing_authorized_is_refused()
    {
        var confirmed = await SeedAsync(CteStatus.Confirmed, 101);

        var e = await Assert.ThrowsAsync<DefaultException>(
            () => new CteXmlZipService(Db, NfeTestSeed.Config()).BuildAsync([confirmed]));

        Assert.Equal("Nenhum CT-e autorizado entre os selecionados.", e.Message);
    }
}
