using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Cte;
using SiagroB1.Application.Services.Transport;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Certificates;

namespace SiagroB1.Application.Tests.Cte;

public class TransportDocumentsEndorseServiceTests : CteEventsTestBase
{
    private const string Password = "segredo-atm";

    private sealed class FakeAverba : IAverbaClient
    {
        public AverbaLoginResult Login { get; set; } = new(true, "tok", null);
        public AverbaCteResult Send { get; set; } = new(true, "PROT-1", "2026-10-23T09:00:00",
            [new AverbaInsurance("AVB1", "11222333000181", "SEGURADORA X", "AP-9", "1500.50", "Rodoviario", "1")], null);
        public int SendCalls { get; private set; }
        public string? SentXml { get; private set; }
        public string? LoginPassword { get; private set; }
        public NfeEnvironment? SentEnvironment { get; private set; }

        public Task<AverbaLoginResult> LoginAsync(NfeEnvironment environment, string user, string password, string code, CancellationToken ct)
        {
            LoginPassword = password;
            return Task.FromResult(Login);
        }

        public Task<AverbaCteResult> SendCteAsync(NfeEnvironment environment, string bearer, string xml, CancellationToken ct)
        {
            SendCalls++;
            SentXml = xml;
            SentEnvironment = environment;
            return Task.FromResult(Send);
        }
    }

    private readonly FakeAverba _averba = new();

    private TransportDocumentsEndorseService Service(string? erp = "STANDALONE") =>
        new(Db, NfeTestSeed.Config(erp), Numbers, _averba);

    private async Task<Guid> SeedEndorsableAsync(CteStatus status = CteStatus.Authorized, bool settings = true)
    {
        var key = await SeedAsync(status);
        Db.Context.TransportDocumentXmls.Add(new TransportDocumentXml { TransportDocumentKey = key, Kind = CteXmlKind.Authorized, Xml = "<cteProc>x</cteProc>" });
        if (settings)
            Db.Context.BranchTransportSettings.Add(new BranchTransportSettings
            {
                BranchCode = "01", AverbaEnabled = true, AverbaUser = "u", AverbaCode = "c",
                AverbaEnvironment = NfeEnvironment.Homologation,
                AverbaPasswordCipher = CertificatePasswordCipher.FromBase64(NfeTestSeed.CertificateKey).Encrypt(Password),
            });
        await Db.SaveChangesAsync();
        return key;
    }

    [Fact]
    public async Task Endorse_authorized_stores_protocol_and_insurances()
    {
        var key = await SeedEndorsableAsync();

        var result = await Service().EndorseAsync(key, "tester");

        Assert.True(result.Success);
        Assert.Equal(Password, _averba.LoginPassword);
        Assert.Equal("<cteProc>x</cteProc>", _averba.SentXml);
        Assert.Equal(NfeEnvironment.Homologation, _averba.SentEnvironment);
        var doc = await Db.Context.TransportDocuments.AsNoTracking().Include(x => x.Endorsements).SingleAsync(x => x.Key == key);
        Assert.Equal(CteEndorsementStatus.Endorsed, doc.EndorsementStatus);
        Assert.Equal("PROT-1", doc.EndorsementProtocol);
        Assert.Equal(new DateTime(2026, 10, 23, 9, 0, 0), doc.EndorsedAt);
        Assert.Null(doc.EndorsementError);
        var insurance = Assert.Single(doc.Endorsements);
        Assert.Equal("AVB1", insurance.EndorsementNumber);
        Assert.Equal("SEGURADORA X", insurance.InsurerName);
        Assert.Equal(1500.50m, insurance.Amount);
        Assert.Equal(0, Numbers.LocksHeld);
    }

    [Fact]
    public async Task Endorse_twice_is_refused()
    {
        var key = await SeedEndorsableAsync();
        await Service().EndorseAsync(key, "tester");

        var result = await Service().EndorseAsync(key, "tester");

        Assert.False(result.Success);
        Assert.Equal("Este CT-e já foi averbado.", result.Message);
        Assert.Equal(1, _averba.SendCalls);
    }

    [Fact]
    public async Task Endorse_not_authorized_is_refused()
    {
        var key = await SeedEndorsableAsync(CteStatus.Confirmed);

        var result = await Service().EndorseAsync(key, "tester");

        Assert.False(result.Success);
        Assert.Equal("Só CT-e autorizado pode ser averbado.", result.Message);
        Assert.Equal(0, _averba.SendCalls);
    }

    [Fact]
    public async Task Endorse_without_branch_credentials_is_refused()
    {
        var key = await SeedEndorsableAsync(settings: false);

        var result = await Service().EndorseAsync(key, "tester");

        Assert.False(result.Success);
        Assert.Equal("Configure a averbação AT&M na filial (Transportadora).", result.Message);
        Assert.Equal(0, _averba.SendCalls);
    }

    [Fact]
    public async Task Endorse_failure_stores_error_and_keeps_none()
    {
        var key = await SeedEndorsableAsync();
        _averba.Send = new AverbaCteResult(false, null, null, [], "CT-e já averbado");

        var result = await Service().EndorseAsync(key, "tester");

        Assert.False(result.Success);
        Assert.Equal("CT-e já averbado", result.Message);
        var doc = await LoadAsync(key);
        Assert.Equal(CteEndorsementStatus.None, doc.EndorsementStatus);
        Assert.Equal("CT-e já averbado", doc.EndorsementError);
        Assert.Null(doc.EndorsementProtocol);
    }

    [Fact]
    public async Task Endorse_login_failure_stores_error()
    {
        var key = await SeedEndorsableAsync();
        _averba.Login = new AverbaLoginResult(false, null, "Senha inválida");

        var result = await Service().EndorseAsync(key, "tester");

        Assert.False(result.Success);
        Assert.Equal("Senha inválida", (await LoadAsync(key)).EndorsementError);
        Assert.Equal(0, _averba.SendCalls);
    }

    [Fact]
    public async Task Concurrent_endorse_is_refused_by_emission_lock()
    {
        var key = await SeedEndorsableAsync();
        Numbers.EmissionBusy = true;

        await Assert.ThrowsAsync<DefaultException>(() => Service().EndorseAsync(key, "tester"));

        Assert.Equal(0, _averba.SendCalls);
    }

    [Fact]
    public async Task Rejected_in_sapb1()
    {
        var key = await SeedEndorsableAsync();

        await Assert.ThrowsAsync<DefaultException>(() => Service("SAPB1").EndorseAsync(key, "tester"));

        Assert.Equal(0, _averba.SendCalls);
    }
}
