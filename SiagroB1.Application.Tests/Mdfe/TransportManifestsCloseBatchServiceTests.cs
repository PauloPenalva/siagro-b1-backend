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
using SiagroB1.Fiscal.Tests.Support;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Mdfe;

/// <summary>Encerramento de MDF-e em lote: cada item é independente e o resumo é por item.</summary>
public class TransportManifestsCloseBatchServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 10, 0, 0, TimeSpan.FromHours(-3));

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();
    private readonly FakeMdfeNumberReservationService _numbers = new(500);
    private readonly FakeMdfeSefazClient _sefaz = new();
    private int _seq = 50;

    private TransportManifestsCloseBatchService Batch(string? erp = null)
    {
        var config = erp is null ? NfeTestSeed.Config() : MdfeTestSupport.Config(erp);
        var contexts = new MdfeServiceContextFactory(_db, new NfeOptions(config));
        var close = new TransportManifestsCloseService(_db, config, _numbers, contexts, _sefaz, () => Now);
        return new TransportManifestsCloseBatchService(_db, config, _numbers, contexts, close);
    }

    private async Task SeedBranchAsync()
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
        await _db.SaveChangesAsync();
    }

    private async Task<Guid> SeedManifestAsync(MdfeStatus status = MdfeStatus.Authorized)
    {
        var number = _seq++;
        var issuedAt = Now.DateTime;
        var m = new TransportManifest
        {
            BranchCode = "01", Status = status, Series = 1, Number = number, RandomCode = "12345678", IssuedAt = issuedAt,
            AccessKey = MdfeAccessKey.Build("35", issuedAt, "12345678000195", 1, number, 1, "12345678"),
            Environment = NfeEnvironment.Homologation, UfStart = "SP", UfEnd = "RJ", ProtocolNumber = FakeMdfeSefazClient.AuthorizationProtocol,
            CteCount = 1,
        };
        m.Ctes.Add(new TransportManifestCte
        {
            LineNum = 1, TransportDocumentKey = Guid.NewGuid(), UnloadingCityIbge = "3304557", UnloadingCityName = "RIO DE JANEIRO",
            CteAccessKey = CteAccessKey.Build("35", new DateTime(2026, 10, 22), "12345678000195", 1, number, 1, "22222222"),
        });
        m.ApplyStatus(status);
        _db.Context.TransportManifests.Add(m);
        await _db.SaveChangesAsync();
        return m.Key;
    }

    private async Task<MdfeStatus> StatusAsync(Guid key) =>
        (await _db.Context.TransportManifests.AsNoTracking().SingleAsync(x => x.Key == key)).Status;

    [Fact]
    public async Task Batch_closes_each_independently()
    {
        await SeedBranchAsync();
        var a = await SeedManifestAsync();
        var b = await SeedManifestAsync();
        _sefaz.CloseResponses.Enqueue(r => FakeMdfeSefazClient.ClosingRegistered(r));
        _sefaz.CloseResponses.Enqueue(r => FakeMdfeSefazClient.ClosingRegistered(r));

        var result = await Batch().CloseBatchAsync([a, b], null, "tester");

        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.True(r.Success));
        Assert.Equal([a, b], result.Select(r => r.Key));
        Assert.Equal([50, 51], result.Select(r => r.Number));
        Assert.Equal(MdfeStatus.Closed, await StatusAsync(a));
        Assert.Equal(MdfeStatus.Closed, await StatusAsync(b));
        Assert.Equal(2, _sefaz.CloseCalls);
        Assert.Equal("3304557", _sefaz.CloseRequests[0].CityIbge);
        Assert.Equal("33", _sefaz.CloseRequests[0].UfCode);
        Assert.Equal(0, _numbers.LocksHeld);
    }

    [Fact]
    public async Task Batch_reports_already_closed_and_timeout_per_item()
    {
        await SeedBranchAsync();
        var closed = await SeedManifestAsync(MdfeStatus.Closed);
        var timeout = await SeedManifestAsync();
        var ok = await SeedManifestAsync();
        var missing = Guid.NewGuid();
        _sefaz.CloseResponses.Enqueue(FakeMdfeSefazClient.NoEventResponse);
        _sefaz.CloseResponses.Enqueue(r => FakeMdfeSefazClient.ClosingRegistered(r));

        var result = await Batch().CloseBatchAsync([closed, timeout, ok, missing], null, "tester");

        Assert.Equal(4, result.Count);
        Assert.False(result[0].Success);
        Assert.Equal(TransportManifestsCloseService.NotAuthorized, result[0].Message);
        Assert.False(result[1].Success);
        Assert.Equal(TransportManifestsCloseService.NoResponse, result[1].Message);
        Assert.True(result[2].Success);
        Assert.False(result[3].Success);
        Assert.Null(result[3].Number);
        Assert.Equal(MdfeStatus.Authorized, await StatusAsync(timeout));
        Assert.Equal(MdfeStatus.Closed, await StatusAsync(ok));
        Assert.Equal(2, _sefaz.CloseCalls);
        Assert.Equal(0, _numbers.LocksHeld);
    }

    [Fact]
    public async Task Batch_reports_busy_lock_per_item()
    {
        await SeedBranchAsync();
        var a = await SeedManifestAsync();
        _numbers.EmissionBusy = true;

        var result = await Batch().CloseBatchAsync([a], null, "tester");

        var item = Assert.Single(result);
        Assert.False(item.Success);
        Assert.Contains("em andamento", item.Message);
        Assert.Equal(0, _sefaz.CloseCalls);
    }

    [Fact]
    public async Task Batch_reports_sefaz_rejection_as_failure()
    {
        await SeedBranchAsync();
        var a = await SeedManifestAsync();
        _sefaz.CloseResponses.Enqueue(_ => FakeMdfeSefazClient.EventRejected(494, "Rejeição: Chave inexistente"));

        var item = Assert.Single(await Batch().CloseBatchAsync([a], null, "tester"));

        Assert.False(item.Success);
        Assert.Contains("494", item.Message);
        Assert.Equal(MdfeStatus.Authorized, await StatusAsync(a));
    }

    [Fact]
    public async Task Batch_future_date_is_reported_per_item()
    {
        await SeedBranchAsync();
        var a = await SeedManifestAsync();

        var item = Assert.Single(await Batch().CloseBatchAsync([a], new DateOnly(2026, 10, 24), "tester"));

        Assert.False(item.Success);
        Assert.Equal(TransportManifestsCloseService.FutureDate, item.Message);
    }

    [Fact]
    public async Task Batch_outside_standalone_is_refused()
    {
        await SeedBranchAsync();
        var a = await SeedManifestAsync();

        await Assert.ThrowsAsync<DefaultException>(() => Batch("SAPB1").CloseBatchAsync([a], null, "tester"));
    }

    [Fact]
    public async Task Batch_unexpected_exception_fails_only_that_item()
    {
        await SeedBranchAsync();
        var a = await SeedManifestAsync();
        var b = await SeedManifestAsync();
        _sefaz.CloseResponses.Enqueue(_ => throw new InvalidOperationException("boom interno"));
        _sefaz.CloseResponses.Enqueue(r => FakeMdfeSefazClient.ClosingRegistered(r));

        var result = await Batch().CloseBatchAsync([a, b], null, "tester");

        Assert.False(result[0].Success);
        Assert.Equal(TransportManifestsCloseBatchService.UnexpectedError, result[0].Message);
        Assert.DoesNotContain("boom", result[0].Message);
        Assert.Equal(50, result[0].Number);
        Assert.True(result[1].Success);
        Assert.Equal(MdfeStatus.Closed, await StatusAsync(b));
        Assert.Equal(0, _numbers.LocksHeld);
    }
}
