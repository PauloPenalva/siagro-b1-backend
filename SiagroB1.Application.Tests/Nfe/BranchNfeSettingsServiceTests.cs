using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Tests.Support;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>Configuração da NF-e por filial e envio do certificado A1 (spec §7.1–§7.3).</summary>
public class BranchNfeSettingsServiceTests
{
    private static readonly string Key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private static IConfiguration Config(string erp = "STANDALONE", bool withKey = true) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Erp"] = erp,
            ["Nfe:CertificateKey"] = withKey ? Key : null,
        }).Build();

    private static async Task<UnitOfWork> SeedAsync(string taxId = "12345678000195")
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch
        {
            Code = "01", BranchName = "CEAGUI", ShortName = "CEAGUI", TaxId = taxId, StateCode = "SP", IssuesNfe = true,
        });
        await db.SaveChangesAsync();
        return db;
    }

    private static BranchNfeSettingsService Service(UnitOfWork db, IConfiguration? config = null, FakeNfeSefazClient? sefaz = null) =>
        new(db, new NfeOptions(config ?? Config()), sefaz ?? new FakeNfeSefazClient());

    [Fact]
    public async Task Get_returns_defaults_when_not_configured()
    {
        var db = await SeedAsync();

        var model = await Service(db).GetAsync("01");

        Assert.Equal(NfeEnvironment.Homologation, model.Environment);
        Assert.Equal(1, model.Series);
        Assert.Equal(1, model.NextNumber);
        Assert.False(model.HasCertificate);
        Assert.True(model.ServerKeyConfigured);
    }

    [Fact]
    public async Task Save_creates_and_then_updates()
    {
        var db = await SeedAsync();
        var service = Service(db);

        await service.SaveAsync("01", NfeEnvironment.Homologation, 1, 10, "tester");
        await service.SaveAsync("01", NfeEnvironment.Production, 2, 500, "tester");

        var saved = await db.Context.BranchNfeSettings.AsNoTracking().SingleAsync();
        Assert.Equal(NfeEnvironment.Production, saved.Environment);
        Assert.Equal(2, saved.Series);
        Assert.Equal(500, saved.NextNumber);
        Assert.Equal("tester", saved.UpdatedBy);
    }

    [Theory]
    [InlineData(-1, 1, "série")]
    [InlineData(1000, 1, "série")]
    [InlineData(1, 0, "próximo número")]
    public async Task Save_validates_series_and_next_number(int series, int next, string expected)
    {
        var db = await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).SaveAsync("01", NfeEnvironment.Homologation, series, next, "tester"));

        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public async Task Upload_stores_the_certificate_and_never_returns_the_password()
    {
        var db = await SeedAsync();

        var model = await Service(db).UploadCertificateAsync("01", TestCertificates.CreatePfx(), TestCertificates.Password, "tester");

        Assert.True(model.HasCertificate);
        Assert.Equal("12345678000195", model.CertificateTaxId);
        var saved = await db.Context.BranchNfeSettings.AsNoTracking().SingleAsync();
        Assert.NotNull(saved.CertificatePfx);
        Assert.Equal(TestCertificates.Password, CertificatePasswordCipher.FromBase64(Key).Decrypt(saved.CertificatePasswordCipher!));
        Assert.DoesNotContain(typeof(Domain.Models.BranchNfeSettingsModel).GetProperties(), p => p.Name.Contains("Password") || p.Name.Contains("Pfx"));
    }

    [Fact]
    public async Task Upload_accepts_a_certificate_of_the_head_office()
    {
        var db = await SeedAsync(taxId: "12345678000276"); // filial 0002 da mesma raiz

        var model = await Service(db).UploadCertificateAsync("01", TestCertificates.CreatePfx("12345678000195"), TestCertificates.Password, "tester");

        Assert.True(model.HasCertificate);
    }

    [Fact]
    public async Task Upload_refuses_wrong_password()
    {
        var db = await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).UploadCertificateAsync("01", TestCertificates.CreatePfx(), "errada", "tester"));

        Assert.Contains("senha", ex.Message);
    }

    [Fact]
    public async Task Upload_refuses_certificate_without_private_key()
    {
        var db = await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service(db).UploadCertificateAsync(
            "01", TestCertificates.CreatePfx(withPrivateKey: false), TestCertificates.Password, "tester"));

        Assert.Contains("chave privada", ex.Message);
    }

    [Fact]
    public async Task Upload_refuses_expired_certificate()
    {
        var db = await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service(db).UploadCertificateAsync(
            "01", TestCertificates.CreatePfx(notAfter: DateTimeOffset.UtcNow.AddDays(-1)), TestCertificates.Password, "tester"));

        Assert.Contains("venceu", ex.Message);
    }

    [Fact]
    public async Task Upload_refuses_certificate_of_another_company()
    {
        var db = await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service(db).UploadCertificateAsync(
            "01", TestCertificates.CreatePfx("99888777000166"), TestCertificates.Password, "tester"));

        Assert.Contains("99888777000166", ex.Message);
    }

    [Fact]
    public async Task Upload_without_server_key_says_what_to_configure()
    {
        var db = await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service(db, Config(withKey: false))
            .UploadCertificateAsync("01", TestCertificates.CreatePfx(), TestCertificates.Password, "tester"));

        Assert.Contains("Nfe:CertificateKey", ex.Message);
    }

    [Fact]
    public async Task Everything_is_refused_outside_standalone()
    {
        var db = await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service(db, Config("SAPB1")).GetAsync("01"));

        Assert.Contains("STANDALONE", ex.Message);
    }

    [Fact]
    public async Task Test_connection_returns_the_sefaz_status()
    {
        var db = await SeedAsync();
        var service = Service(db);
        await service.UploadCertificateAsync("01", TestCertificates.CreatePfx(), TestCertificates.Password, "tester");

        var status = await service.TestConnectionAsync("01");

        Assert.Equal(107, status.StatusCode);
        Assert.Equal("Serviço em Operação", status.Reason);
    }

    [Fact]
    public async Task Open_without_branch_state_is_refused_before_loading_the_certificate()
    {
        var db = await SeedAsync();
        var service = Service(db);
        await service.UploadCertificateAsync("01", TestCertificates.CreatePfx(), TestCertificates.Password, "tester");
        (await db.Context.Branchs.SingleAsync()).StateCode = null;
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.TestConnectionAsync("01"));

        Assert.Contains("sem UF", ex.Message);
    }

    [Fact]
    public async Task Test_connection_without_certificate_is_refused()
    {
        var db = await SeedAsync();
        await Service(db).SaveAsync("01", NfeEnvironment.Homologation, 1, 1, "tester");

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service(db).TestConnectionAsync("01"));

        Assert.Contains("certificado", ex.Message);
    }
}
