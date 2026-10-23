using SiagroB1.Application.Services.Mdfe;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Mdfe;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Fiscal.Tests.Support;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Mdfe;

/// <summary>MDF-e não encerrados na SEFAZ cruzados com os MDF-e do sistema.</summary>
public class MdfeNotClosedQueryServiceTests
{
    private const string Cnpj = "12345678000195";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();
    private readonly FakeMdfeSefazClient _sefaz = new();

    private MdfeNotClosedQueryService Service(string? erp = null)
    {
        var config = erp is null ? NfeTestSeed.Config() : MdfeTestSupport.Config(erp);
        return new MdfeNotClosedQueryService(_db, config, new MdfeServiceContextFactory(_db, new NfeOptions(config)), _sefaz);
    }

    private static string KeyOf(int number) =>
        MdfeAccessKey.Build("35", new DateTime(2026, 10, 23), Cnpj, 1, number, 1, "12345678");

    private async Task<Guid> SeedAsync(params (int Number, MdfeStatus Status)[] manifests)
    {
        _db.Context.Branchs.Add(new Branch
        {
            Code = "01", BranchName = "TRANSPORTADORA", LegalName = "CEAGUI CEREAIS LTDA", TaxId = Cnpj,
            StateCode = "SP", StateRegistration = "387000000110", TaxRegime = TaxRegime.Normal,
            Street = "RODOVIA SP 258", StreetNumber = "KM 290", District = "ZONA RURAL", MunicipalityCode = "3521705",
            ZipCode = "18440000",
        });
        _db.Context.BranchMdfeSettings.Add(new BranchMdfeSettings { BranchCode = "01", Enabled = true });
        _db.Context.BranchNfeSettings.Add(new BranchNfeSettings
        {
            BranchCode = "01",
            CertificatePfx = TestCertificates.CreatePfx(Cnpj),
            CertificatePasswordCipher = CertificatePasswordCipher.FromBase64(NfeTestSeed.CertificateKey).Encrypt(TestCertificates.Password),
            CertificateValidUntil = DateTime.Now.AddYears(1),
        });
        Guid first = Guid.Empty;
        foreach (var (number, status) in manifests)
        {
            var m = new TransportManifest
            {
                BranchCode = "01", Status = status, Series = 1, Number = number, RandomCode = "12345678",
                IssuedAt = new DateTime(2026, 10, 23), AccessKey = KeyOf(number), TractionPlate = "ABC1D23",
                Environment = NfeEnvironment.Homologation, UfStart = "SP", UfEnd = "RJ",
            };
            _db.Context.TransportManifests.Add(m);
            if (first == Guid.Empty) first = m.Key;
        }
        await _db.SaveChangesAsync();
        return first;
    }

    [Fact]
    public async Task NotClosed_matches_known_manifests()
    {
        await SeedAsync((50, MdfeStatus.Authorized), (51, MdfeStatus.Processing));
        _sefaz.NotClosedResponses.Enqueue(_ => [new MdfeNotClosed(KeyOf(50), "111"), new MdfeNotClosed(KeyOf(51), "222")]);

        var list = await Service().ListAsync("01");

        Assert.Equal(Cnpj, Assert.Single(_sefaz.NotClosedQueries));
        Assert.Equal(2, list.Count);
        var a = list.Single(x => x.AccessKey == KeyOf(50));
        Assert.Equal("111", a.Protocol);
        Assert.NotNull(a.ManifestKey);
        Assert.Equal(50, a.Number);
        Assert.Equal(1, a.Series);
        Assert.Equal(MdfeStatus.Authorized, a.Status);
        Assert.Equal("ABC1D23", a.TractionPlate);
        Assert.Equal(MdfeStatus.Processing, list.Single(x => x.AccessKey == KeyOf(51)).Status);
    }

    [Fact]
    public async Task NotClosed_unknown_key_is_reported_outside_system()
    {
        await SeedAsync((50, MdfeStatus.Authorized));
        _sefaz.NotClosedResponses.Enqueue(_ => [new MdfeNotClosed(KeyOf(99), "999")]);

        var item = Assert.Single(await Service().ListAsync("01"));

        Assert.Equal(KeyOf(99), item.AccessKey);
        Assert.Null(item.ManifestKey);
        Assert.Null(item.Number);
        Assert.Null(item.Series);
        Assert.Null(item.Status);
        Assert.Null(item.TractionPlate);
    }

    [Fact]
    public async Task NotClosed_none_returns_empty()
    {
        await SeedAsync((50, MdfeStatus.Authorized));
        _sefaz.NotClosedResponses.Enqueue(_ => []);

        Assert.Empty(await Service().ListAsync("01"));
    }

    [Fact]
    public async Task NotClosed_sefaz_refusal_becomes_default_exception_with_code_and_reason()
    {
        await SeedAsync((50, MdfeStatus.Authorized));
        _sefaz.NotClosedResponses.Enqueue(_ => throw new MdfeSefazRejectedException(215, "Rejeição: Falha no schema"));

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service().ListAsync("01"));
        Assert.Contains("215", e.Message);
        Assert.Contains("Falha no schema", e.Message);
    }

    [Fact]
    public async Task NotClosed_no_response_becomes_default_exception()
    {
        await SeedAsync((50, MdfeStatus.Authorized));
        _sefaz.NotClosedResponses.Enqueue(_ => throw new NfeCommunicationException("Tempo esgotado."));

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service().ListAsync("01"));
        Assert.StartsWith("Sem resposta da SEFAZ", e.Message);
    }

    [Fact]
    public async Task NotClosed_requires_standalone()
    {
        await Assert.ThrowsAsync<DefaultException>(() => Service("SAPB1").ListAsync("01"));
        Assert.Equal(0, _sefaz.NotClosedCalls);
    }
}
