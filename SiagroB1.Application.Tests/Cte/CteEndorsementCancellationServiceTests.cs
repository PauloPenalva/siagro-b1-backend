using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.Cte;
using SiagroB1.Application.Services.Transport;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Certificates;

namespace SiagroB1.Application.Tests.Cte;

public class CteEndorsementCancellationServiceTests : CteEventsTestBase
{
    private sealed class FakeAverba : IAverbaClient
    {
        public AverbaLoginResult Login { get; set; } = new(true, "tok", null);
        public AverbaCteResult Send { get; set; } = new(true, "CANC-1", null, [], null);
        public int SendCalls { get; private set; }
        public string? SentXml { get; private set; }

        public Task<AverbaLoginResult> LoginAsync(NfeEnvironment environment, string user, string password, string code, CancellationToken ct) =>
            Task.FromResult(Login);

        public Task<AverbaCteResult> SendCteAsync(NfeEnvironment environment, string bearer, string xml, CancellationToken ct)
        {
            SendCalls++;
            SentXml = xml;
            return Task.FromResult(Send);
        }
    }

    private readonly FakeAverba _averba = new();

    private CteEndorsementCancellationService Service() =>
        new(Db, NfeTestSeed.Config("STANDALONE"), Numbers, _averba, NullLogger<CteEndorsementCancellationService>.Instance);

    private async Task<Guid> SeedWithEndorsementAsync(CteEndorsementStatus endorsement)
    {
        var key = await SeedAsync(CteStatus.Cancelled);
        var doc = await Db.Context.TransportDocuments.SingleAsync(x => x.Key == key);
        doc.EndorsementStatus = endorsement;
        Db.Context.TransportDocumentXmls.Add(new TransportDocumentXml { TransportDocumentKey = key, Kind = CteXmlKind.Cancellation, Xml = "<procEventoCTe>c</procEventoCTe>" });
        Db.Context.BranchTransportSettings.Add(new BranchTransportSettings
        {
            BranchCode = "01", AverbaEnabled = true, AverbaUser = "u", AverbaCode = "c",
            AverbaEnvironment = NfeEnvironment.Homologation,
            AverbaPasswordCipher = CertificatePasswordCipher.FromBase64(NfeTestSeed.CertificateKey).Encrypt("s"),
        });
        await Db.SaveChangesAsync();
        return key;
    }

    private Task<TransportDocument> TrackedAsync(Guid key) =>
        Db.Context.TransportDocuments.Include(x => x.Xmls).SingleAsync(x => x.Key == key);

    [Fact]
    public async Task Cancelled_endorsed_cte_sends_cancellation_event_to_atm()
    {
        var key = await SeedWithEndorsementAsync(CteEndorsementStatus.Endorsed);

        await Service().CancelAsync(await TrackedAsync(key));

        Assert.Equal("<procEventoCTe>c</procEventoCTe>", _averba.SentXml);
        var doc = await LoadAsync(key);
        Assert.Equal(CteEndorsementStatus.Cancelled, doc.EndorsementStatus);
        Assert.Equal("CANC-1", doc.EndorsementCancellationProtocol);
        Assert.Null(doc.EndorsementError);
    }

    [Fact]
    public async Task Atm_failure_leaves_cancellation_pending_without_throwing()
    {
        var key = await SeedWithEndorsementAsync(CteEndorsementStatus.Endorsed);
        _averba.Send = new AverbaCteResult(false, null, null, [], "AT&M fora do ar");

        await Service().CancelAsync(await TrackedAsync(key));

        var doc = await LoadAsync(key);
        Assert.Equal(CteEndorsementStatus.CancellationPending, doc.EndorsementStatus);
        Assert.Equal("AT&M fora do ar", doc.EndorsementError);
    }

    [Fact]
    public async Task Login_failure_leaves_cancellation_pending()
    {
        var key = await SeedWithEndorsementAsync(CteEndorsementStatus.Endorsed);
        _averba.Login = new AverbaLoginResult(false, null, "Senha inválida");

        await Service().CancelAsync(await TrackedAsync(key));

        var doc = await LoadAsync(key);
        Assert.Equal(CteEndorsementStatus.CancellationPending, doc.EndorsementStatus);
        Assert.Equal("Senha inválida", doc.EndorsementError);
        Assert.Equal(0, _averba.SendCalls);
    }

    [Fact]
    public async Task Resend_pending_cancellation_succeeds()
    {
        var key = await SeedWithEndorsementAsync(CteEndorsementStatus.CancellationPending);

        var result = await Service().ResendAsync(key, "tester");

        Assert.True(result.Success);
        var doc = await LoadAsync(key);
        Assert.Equal(CteEndorsementStatus.Cancelled, doc.EndorsementStatus);
        Assert.Equal("CANC-1", doc.EndorsementCancellationProtocol);
        Assert.Equal(0, Numbers.LocksHeld);
    }

    [Fact]
    public async Task Resend_refused_when_not_pending()
    {
        var key = await SeedWithEndorsementAsync(CteEndorsementStatus.Endorsed);

        var result = await Service().ResendAsync(key, "tester");

        Assert.False(result.Success);
        Assert.Equal(0, _averba.SendCalls);
    }

    [Fact]
    public async Task Resend_is_refused_by_emission_lock_when_busy()
    {
        var key = await SeedWithEndorsementAsync(CteEndorsementStatus.CancellationPending);
        Numbers.EmissionBusy = true;

        await Assert.ThrowsAsync<DefaultException>(() => Service().ResendAsync(key, "tester"));
    }

    [Fact]
    public async Task Not_endorsed_does_nothing()
    {
        var key = await SeedWithEndorsementAsync(CteEndorsementStatus.None);

        await Service().CancelAsync(await TrackedAsync(key));

        Assert.Equal(0, _averba.SendCalls);
        Assert.Equal(CteEndorsementStatus.None, (await LoadAsync(key)).EndorsementStatus);
    }
}
