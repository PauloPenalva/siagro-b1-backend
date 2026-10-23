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

/// <summary>Cancelamento do MDF-e (110111) com a SEFAZ simulada.</summary>
public class TransportManifestsCancelServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 10, 0, 0, TimeSpan.FromHours(-3));
    private const string Justification = "Emitido por engano, viagem nao realizada";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();
    private readonly FakeMdfeNumberReservationService _numbers = new(50);
    private readonly FakeMdfeSefazClient _sefaz = new();
    private DateTimeOffset _clock = Now;

    private TransportManifestsCancelService Cancel()
    {
        var config = NfeTestSeed.Config();
        return new TransportManifestsCancelService(
            _db, config, _numbers, new MdfeServiceContextFactory(_db, new NfeOptions(config)), _sefaz, () => _clock);
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
            CteCount = 2, AuthorizedAt = Now.DateTime,
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

    [Theory]
    [InlineData("curta demais")]
    [InlineData("               ")]
    public async Task Cancel_short_justification_is_rejected(string text)
    {
        var key = await SeedAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Cancel().CancelAsync(key, text, "tester"));

        Assert.Equal("A justificativa do cancelamento tem de 15 a 255 caracteres.", e.Message);
        Assert.Equal(0, _sefaz.CancelCalls);
        Assert.Equal(MdfeStatus.Authorized, (await LoadAsync(key)).Status);
    }

    [Fact]
    public async Task Cancel_long_justification_is_rejected()
    {
        var key = await SeedAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Cancel().CancelAsync(key, new string('x', 256), "tester"));

        Assert.Equal("A justificativa do cancelamento tem de 15 a 255 caracteres.", e.Message);
        Assert.Equal(0, _sefaz.CancelCalls);
    }

    [Fact]
    public async Task Cancel_after_24h_is_rejected()
    {
        var key = await SeedAsync();
        _clock = Now.AddHours(24).AddMinutes(1);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Cancel().CancelAsync(key, Justification, "tester"));

        Assert.Equal("O prazo de cancelamento do MDF-e (24 horas) acabou.", e.Message);
        Assert.Equal(0, _sefaz.CancelCalls);
        Assert.Equal(0, _numbers.LocksHeld);
    }

    [Fact]
    public async Task Cancel_closed_is_rejected()
    {
        var key = await SeedAsync(MdfeStatus.Closed);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Cancel().CancelAsync(key, Justification, "tester"));

        Assert.Equal(TransportManifestsCancelService.NotAuthorized, e.Message);
        Assert.Equal(0, _sefaz.CancelCalls);
    }

    [Fact]
    public async Task Cancel_135_marks_cancelled_and_frees_ctes()
    {
        var key = await SeedAsync();
        _sefaz.CancelResponses.Enqueue(r => FakeMdfeSefazClient.CancellationRegistered(r));

        var result = await Cancel().CancelAsync(key, Justification, "tester");

        Assert.Equal(135, result.StatusCode);
        var sent = Assert.Single(_sefaz.CancelRequests);
        Assert.Equal(Justification, sent.Justification);
        Assert.Equal(FakeMdfeSefazClient.AuthorizationProtocol, sent.Protocol);
        Assert.Equal(Now, sent.EventAt);
        var m = await LoadAsync(key);
        Assert.Equal(MdfeStatus.Cancelled, m.Status);
        Assert.All(m.Ctes, c => Assert.False(c.IsActive));
        Assert.NotNull(m.CancelledAt);
        Assert.Contains("<procEventoMDFe", m.Xmls.Single(x => x.Kind == MdfeXmlKind.Cancellation).Xml);
        var ev = Assert.Single(m.Events);
        Assert.Equal(MdfeEventType.Cancellation, ev.Type);
        Assert.Equal(Justification, ev.Justification);
        Assert.Equal("135", ev.StatusCode);
        Assert.Equal(0, _numbers.LocksHeld);
    }

    [Fact]
    public async Task Cancel_sefaz_says_closed_consults_and_marks_closed()
    {
        var key = await SeedAsync();
        _sefaz.CancelResponses.Enqueue(_ => FakeMdfeSefazClient.EventRejected(641, "Rejeição: MDF-e encerrado"));
        _sefaz.ConsultResponses.Enqueue(_ => new NfeSefazResult(132, "MDF-e encerrado"));

        var result = await Cancel().CancelAsync(key, Justification, "tester");

        Assert.Equal(641, result.StatusCode);
        var m = await LoadAsync(key);
        Assert.Equal(MdfeStatus.Closed, m.Status);
        Assert.All(m.Ctes, c => Assert.False(c.IsActive));
    }

    [Fact]
    public async Task Cancel_rejection_keeps_authorized()
    {
        var key = await SeedAsync();
        _sefaz.CancelResponses.Enqueue(_ => FakeMdfeSefazClient.EventRejected(494, "Rejeição: Chave inexistente"));
        _sefaz.ConsultResponses.Enqueue(_ => new NfeSefazResult(100, "Autorizado"));

        var result = await Cancel().CancelAsync(key, Justification, "tester");

        Assert.Equal(494, result.StatusCode);
        Assert.Equal(MdfeStatus.Authorized, (await LoadAsync(key)).Status);
    }

    [Fact]
    public async Task Cancel_without_sefaz_response_keeps_authorized()
    {
        var key = await SeedAsync();
        _sefaz.CancelResponses.Enqueue(FakeMdfeSefazClient.NoEventResponse);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Cancel().CancelAsync(key, Justification, "tester"));

        Assert.Equal(TransportManifestsCancelService.NoResponse, e.Message);
        Assert.Equal(MdfeStatus.Authorized, (await LoadAsync(key)).Status);
        Assert.Equal(0, _numbers.LocksHeld);
    }
}
