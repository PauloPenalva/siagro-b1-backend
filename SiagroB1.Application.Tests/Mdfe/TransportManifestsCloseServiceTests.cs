using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Mdfe;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Cte;
using SiagroB1.Fiscal.Mdfe;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Fiscal.Tests.Support;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Mdfe;

/// <summary>Encerramento do MDF-e (110112) com a SEFAZ simulada.</summary>
public class TransportManifestsCloseServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 10, 0, 0, TimeSpan.FromHours(-3));

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();
    private readonly FakeMdfeNumberReservationService _numbers = new(50);
    private readonly FakeMdfeSefazClient _sefaz = new();

    private TransportManifestsCloseService Close()
    {
        var config = NfeTestSeed.Config();
        return new TransportManifestsCloseService(
            _db, config, _numbers, new MdfeServiceContextFactory(_db, new NfeOptions(config)), _sefaz, () => Now);
    }

    private async Task<Guid> SeedAsync(MdfeStatus status = MdfeStatus.Authorized)
    {
        _db.Context.Branchs.Add(new Branch
        {
            Code = "01", BranchName = "TRANSPORTADORA", LegalName = "CEAGUI CEREAIS LTDA", TaxId = "12345678000195",
            StateCode = "SP", StateRegistration = "387000000110", TaxRegime = TaxRegime.Normal,
            Street = "RODOVIA SP 258", StreetNumber = "KM 290", District = "ZONA RURAL", MunicipalityCode = "3521705",
            ZipCode = "18440000",
        });
        _db.Context.BranchMdfeSettings.Add(new BranchMdfeSettings { BranchCode = "01", Enabled = true });
        _db.Context.BranchNfeSettings.Add(new BranchNfeSettings
        {
            BranchCode = "01",
            CertificatePfx = TestCertificates.CreatePfx("12345678000195"),
            CertificatePasswordCipher = CertificatePasswordCipher.FromBase64(NfeTestSeed.CertificateKey).Encrypt(TestCertificates.Password),
            CertificateValidUntil = DateTime.Now.AddYears(1),
        });

        var issuedAt = Now.DateTime;
        var m = new TransportManifest
        {
            BranchCode = "01", Status = status, Series = 1, Number = 50, RandomCode = "12345678", IssuedAt = issuedAt,
            AccessKey = MdfeAccessKey.Build("35", issuedAt, "12345678000195", 1, 50, 1, "12345678"),
            Environment = NfeEnvironment.Homologation, UfStart = "SP", UfEnd = "RJ", ProtocolNumber = FakeMdfeSefazClient.AuthorizationProtocol,
            CteCount = 2,
        };
        m.Ctes.Add(new TransportManifestCte
        {
            LineNum = 1, TransportDocumentKey = Guid.NewGuid(), UnloadingCityIbge = "3550308", UnloadingCityName = "SAO PAULO",
            CteAccessKey = CteAccessKey.Build("35", new DateTime(2026, 10, 22), "12345678000195", 1, 7, 1, "11111111"),
        });
        m.Ctes.Add(new TransportManifestCte
        {
            LineNum = 2, TransportDocumentKey = Guid.NewGuid(), UnloadingCityIbge = "3304557", UnloadingCityName = "RIO DE JANEIRO",
            CteAccessKey = CteAccessKey.Build("35", new DateTime(2026, 10, 22), "12345678000195", 1, 8, 1, "22222222"),
        });
        m.ApplyStatus(status);
        _db.Context.TransportManifests.Add(m);
        await _db.SaveChangesAsync();
        return m.Key;
    }

    private Task<TransportManifest> LoadAsync(Guid key) =>
        _db.Context.TransportManifests.AsNoTracking().Include(x => x.Xmls).Include(x => x.Ctes).Include(x => x.Events)
            .SingleAsync(x => x.Key == key);

    [Fact]
    public async Task Close_defaults_to_last_unloading_city()
    {
        var key = await SeedAsync();
        _sefaz.CloseResponses.Enqueue(r => FakeMdfeSefazClient.ClosingRegistered(r));

        await Close().CloseAsync(key, null, null, null, "tester");

        var sent = Assert.Single(_sefaz.CloseRequests);
        Assert.Equal("3304557", sent.CityIbge);
        Assert.Equal("RJ", sent.UfCode);
        Assert.Equal(new DateOnly(2026, 10, 23), sent.ClosedOn);
        Assert.Equal(FakeMdfeSefazClient.AuthorizationProtocol, sent.Protocol);
        Assert.Equal("12345678000195", sent.IssuerCnpj);
        Assert.Equal(Now, sent.EventAt);
    }

    [Fact]
    public async Task Close_135_marks_closed_and_frees_ctes()
    {
        var key = await SeedAsync();
        _sefaz.CloseResponses.Enqueue(r => FakeMdfeSefazClient.ClosingRegistered(r));

        var result = await Close().CloseAsync(key, new DateOnly(2026, 10, 22), "SP", "3550308", "tester");

        Assert.Equal(135, result.StatusCode);
        var m = await LoadAsync(key);
        Assert.Equal(MdfeStatus.Closed, m.Status);
        Assert.All(m.Ctes, c => Assert.False(c.IsActive));
        Assert.Equal(new DateOnly(2026, 10, 22), m.ClosedOn);
        Assert.Equal("SP", m.ClosingUf);
        Assert.Equal("3550308", m.ClosingCityIbge);
        Assert.NotNull(m.ClosedAt);
        Assert.Contains("<procEventoMDFe", m.Xmls.Single(x => x.Kind == MdfeXmlKind.Closing).Xml);
        var ev = Assert.Single(m.Events);
        Assert.Equal(MdfeEventType.Closing, ev.Type);
        Assert.Equal(FakeMdfeSefazClient.EventProtocol, ev.ProtocolNumber);
        Assert.Equal("135", ev.StatusCode);
        Assert.Equal(0, _numbers.LocksHeld);
    }

    [Fact]
    public async Task Close_closed_manifest_is_refused()
    {
        var key = await SeedAsync(MdfeStatus.Closed);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Close().CloseAsync(key, null, null, null, "tester"));

        Assert.Equal(TransportManifestsCloseService.NotAuthorized, e.Message);
        Assert.Equal(0, _sefaz.CloseCalls);
    }

    [Fact]
    public async Task Close_future_date_is_refused()
    {
        var key = await SeedAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Close().CloseAsync(key, new DateOnly(2026, 10, 24), null, null, "tester"));

        Assert.Equal("A data de encerramento não pode ser futura.", e.Message);
        Assert.Equal(0, _sefaz.CloseCalls);
    }

    [Fact]
    public async Task Close_timeout_keeps_authorized()
    {
        var key = await SeedAsync();
        _sefaz.CloseResponses.Enqueue(FakeMdfeSefazClient.NoEventResponse);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Close().CloseAsync(key, null, null, null, "tester"));

        Assert.Equal("Sem resposta da SEFAZ: tente encerrar de novo.", e.Message);
        var m = await LoadAsync(key);
        Assert.Equal(MdfeStatus.Authorized, m.Status);
        Assert.All(m.Ctes, c => Assert.True(c.IsActive));
        Assert.Empty(m.Events);
        Assert.Equal(0, _numbers.LocksHeld);
    }

    [Fact]
    public async Task Close_duplicate_event_marks_closed()
    {
        var key = await SeedAsync();
        _sefaz.CloseResponses.Enqueue(_ => FakeMdfeSefazClient.EventRejected(631));
        _sefaz.ConsultResponses.Enqueue(_ => new NfeSefazResult(132, "MDF-e encerrado"));

        var result = await Close().CloseAsync(key, null, null, null, "tester");

        Assert.Equal(135, result.StatusCode);
        var m = await LoadAsync(key);
        Assert.Equal(MdfeStatus.Closed, m.Status);
        Assert.All(m.Ctes, c => Assert.False(c.IsActive));
    }

    [Fact]
    public async Task Close_rejection_keeps_authorized()
    {
        var key = await SeedAsync();
        _sefaz.CloseResponses.Enqueue(_ => FakeMdfeSefazClient.EventRejected(494, "Rejeição: Chave inexistente"));

        var result = await Close().CloseAsync(key, null, null, null, "tester");

        Assert.Equal(494, result.StatusCode);
        Assert.Equal(MdfeStatus.Authorized, (await LoadAsync(key)).Status);
    }
}
