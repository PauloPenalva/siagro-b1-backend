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

/// <summary>Inclusão de condutor no MDF-e autorizado (evento 110114) com a SEFAZ simulada.</summary>
public class TransportManifestsIncludeDriverServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 10, 0, 0, TimeSpan.FromHours(-3));
    private const string ValidCpf = "52998224725";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();
    private readonly FakeMdfeNumberReservationService _numbers = new(50);
    private readonly FakeMdfeSefazClient _sefaz = new();

    private TransportManifestsIncludeDriverService Service(string? erp = null)
    {
        var config = erp is null ? NfeTestSeed.Config() : MdfeTestSupport.Config(erp);
        return new TransportManifestsIncludeDriverService(
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
            CteCount = 2, DriverCpf = "11144477735",
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
        _db.Context.TransportManifests.AsNoTracking().Include(x => x.Xmls).Include(x => x.Drivers).Include(x => x.Events)
            .SingleAsync(x => x.Key == key);

    [Fact]
    public async Task Include_driver_invalid_cpf_is_rejected()
    {
        var key = await SeedAsync();
        var e = await Assert.ThrowsAsync<DefaultException>(() => Service().IncludeDriverAsync(key, "11111111111", "JOAO", "tester"));
        Assert.Equal(TransportManifestsIncludeDriverService.InvalidCpf, e.Message);
        Assert.Equal(0, _sefaz.IncludeDriverCalls);
    }

    [Fact]
    public async Task Include_driver_duplicate_is_rejected()
    {
        var key = await SeedAsync();
        var e = await Assert.ThrowsAsync<DefaultException>(() => Service().IncludeDriverAsync(key, "111.444.777-35", "JOAO", "tester"));
        Assert.Equal("O condutor já está no MDF-e.", e.Message);
        Assert.Equal(0, _sefaz.IncludeDriverCalls);
    }

    [Fact]
    public async Task Include_driver_increments_sequence_and_stores_xml()
    {
        var key = await SeedAsync();
        _sefaz.IncludeDriverResponses.Enqueue(r => FakeMdfeSefazClient.DriverIncluded(r));
        await Service().IncludeDriverAsync(key, ValidCpf, "JOAO DA SILVA", "tester");

        var second = "39053344705";
        _sefaz.IncludeDriverResponses.Enqueue(r => FakeMdfeSefazClient.DriverIncluded(r));
        await Service().IncludeDriverAsync(key, second, "MARIA", "tester");

        Assert.Equal(new[] { 1, 2 }, _sefaz.IncludeDriverRequests.Select(x => x.Sequence));
        Assert.Equal(Now, _sefaz.IncludeDriverRequests[0].EventAt);
        var m = await LoadAsync(key);
        Assert.Equal(MdfeStatus.Authorized, m.Status);
        var d = Assert.Single(m.Drivers, x => x.Cpf == ValidCpf);
        Assert.True(d.AddedByEvent);
        Assert.Equal(1, d.EventSequence);
        Assert.Equal(2, m.Drivers.Single(x => x.Cpf == second).EventSequence);
        Assert.Equal(2, m.Events.Count(x => x.Type == MdfeEventType.DriverInclusion));
        Assert.Equal(2, m.Xmls.Count(x => x.Kind == MdfeXmlKind.DriverInclusion));
    }

    [Fact]
    public async Task Include_driver_rejected_by_sefaz_stores_nothing()
    {
        var key = await SeedAsync();
        _sefaz.IncludeDriverResponses.Enqueue(r => FakeMdfeSefazClient.EventRejected(494, "Rejeição"));
        await Service().IncludeDriverAsync(key, ValidCpf, "JOAO", "tester");
        var m = await LoadAsync(key);
        Assert.Empty(m.Drivers);
        Assert.Empty(m.Events);
    }

    [Fact]
    public async Task Include_driver_without_response_is_reported()
    {
        var key = await SeedAsync();
        _sefaz.IncludeDriverResponses.Enqueue(_ => throw new NfeCommunicationException("timeout"));
        var e = await Assert.ThrowsAsync<DefaultException>(() => Service().IncludeDriverAsync(key, ValidCpf, "JOAO", "tester"));
        Assert.StartsWith("Sem resposta da SEFAZ: ", e.Message);
    }

    [Theory]
    [InlineData(MdfeStatus.Closed)]
    [InlineData(MdfeStatus.Cancelled)]
    [InlineData(MdfeStatus.Draft)]
    public async Task Include_driver_only_when_authorized(MdfeStatus status)
    {
        var key = await SeedAsync(status);
        var e = await Assert.ThrowsAsync<DefaultException>(() => Service().IncludeDriverAsync(key, ValidCpf, "JOAO", "tester"));
        Assert.Equal(TransportManifestsIncludeDriverService.NotAuthorized, e.Message);
        Assert.Equal(0, _sefaz.IncludeDriverCalls);
    }
}
