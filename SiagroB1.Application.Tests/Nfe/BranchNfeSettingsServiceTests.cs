using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
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

    private static BranchNfeSeriesInput[] One(int series = 1, int next = 1, string description = "Padrão") =>
        [new BranchNfeSeriesInput(series, description, next)];

    private static Task<BranchNfeSettingsModel> SaveOne(BranchNfeSettingsService service, NfeEnvironment environment, int series, int next) =>
        service.SaveAsync("01", environment, series, One(series, next), "tester");

    [Fact]
    public async Task Get_returns_one_default_series_when_not_configured()
    {
        var db = await SeedAsync();

        var model = await Service(db).GetAsync("01");

        Assert.Equal(NfeEnvironment.Homologation, model.Environment);
        Assert.Equal(1, model.DefaultSeries);
        var row = Assert.Single(model.SeriesList);
        Assert.Equal((1, "Padrão", 1, false), (row.Series, row.Description, row.NextNumber, row.IsUsed));
        Assert.False(model.HasCertificate);
        Assert.True(model.ServerKeyConfigured);
    }

    [Fact]
    public async Task Save_creates_and_updates_the_series_list()
    {
        var db = await SeedAsync();
        var service = Service(db);

        await service.SaveAsync("01", NfeEnvironment.Homologation, 300,
            [new(300, "Remessa", 1), new(1, "Venda", 10)], "tester");
        var model = await service.GetAsync("01");

        Assert.Equal(300, model.DefaultSeries);
        Assert.Equal([1, 300], model.SeriesList.Select(x => x.Series));
        Assert.Equal(["Venda", "Remessa"], model.SeriesList.Select(x => x.Description));
        Assert.Equal([10, 1], model.SeriesList.Select(x => x.NextNumber));
        var saved = await db.Context.BranchNfeSettings.AsNoTracking().SingleAsync();
        Assert.Equal("tester", saved.UpdatedBy);
    }

    [Fact]
    public async Task Save_refuses_lowering_next_number_in_the_same_environment()
    {
        var db = await SeedAsync();
        var service = Service(db);
        await SaveOne(service, NfeEnvironment.Homologation, 1, 10);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => SaveOne(service, NfeEnvironment.Homologation, 1, 5));

        Assert.Contains("O próximo número da série 1 não pode voltar", ex.Message);
        Assert.Equal(10, (await db.Context.BranchNfeSeries.AsNoTracking().SingleAsync()).NextNumber);
    }

    [Fact]
    public async Task Save_refuses_lowering_a_non_default_series_too()
    {
        var db = await SeedAsync();
        var service = Service(db);
        await service.SaveAsync("01", NfeEnvironment.Homologation, 1, [new(1, "Venda", 1), new(300, "Remessa", 20)], "tester");

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            service.SaveAsync("01", NfeEnvironment.Homologation, 1, [new(1, "Venda", 1), new(300, "Remessa", 5)], "tester"));

        Assert.Contains("O próximo número da série 300 não pode voltar", ex.Message);
    }

    [Fact]
    public async Task Save_allows_any_next_number_when_the_environment_changes()
    {
        var db = await SeedAsync();
        var service = Service(db);
        await SaveOne(service, NfeEnvironment.Homologation, 1, 50);

        var model = await SaveOne(service, NfeEnvironment.Production, 1, 1);

        Assert.Equal(1, Assert.Single(model.SeriesList).NextNumber);
        Assert.Equal(NfeEnvironment.Production, model.Environment);
    }

    [Fact]
    public async Task Save_allows_a_new_series_with_any_number_and_raising_existing_ones()
    {
        var db = await SeedAsync();
        var service = Service(db);
        await SaveOne(service, NfeEnvironment.Homologation, 1, 50);

        var model = await service.SaveAsync("01", NfeEnvironment.Homologation, 1, [new(1, "Padrão", 60), new(2, "Nova", 1)], "tester");

        Assert.Equal([60, 1], model.SeriesList.Select(x => x.NextNumber));
    }

    [Fact]
    public async Task Save_refuses_a_default_outside_the_list()
    {
        var db = await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).SaveAsync("01", NfeEnvironment.Homologation, 2, One(), "tester"));

        Assert.Equal("Escolha a série padrão entre as séries cadastradas.", ex.Message);
    }

    [Fact]
    public async Task Save_refuses_duplicate_series()
    {
        var db = await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service(db).SaveAsync(
            "01", NfeEnvironment.Homologation, 1, [new(1, "A", 1), new(1, "B", 2)], "tester"));

        Assert.Equal("A série 1 aparece mais de uma vez.", ex.Message);
    }

    [Fact]
    public async Task Save_refuses_empty_list()
    {
        var db = await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).SaveAsync("01", NfeEnvironment.Homologation, 1, [], "tester"));

        Assert.Equal("Cadastre ao menos uma série.", ex.Message);
    }

    [Fact]
    public async Task Save_refuses_blank_description()
    {
        var db = await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service(db).SaveAsync(
            "01", NfeEnvironment.Homologation, 1, [new(1, "Venda", 1), new(300, "  ", 1)], "tester"));

        Assert.Equal("Informe a descrição da série 300.", ex.Message);
    }

    [Fact]
    public async Task Save_trims_the_description_and_refuses_more_than_60_characters()
    {
        var db = await SeedAsync();
        var service = Service(db);

        var model = await service.SaveAsync("01", NfeEnvironment.Homologation, 1, One(description: "  Venda  "), "tester");
        Assert.Equal("Venda", Assert.Single(model.SeriesList).Description);

        await Assert.ThrowsAsync<DefaultException>(() =>
            service.SaveAsync("01", NfeEnvironment.Homologation, 1, One(description: new string('x', 61)), "tester"));
    }

    [Theory]
    [InlineData(-1, 1, "A série da NF-e vai de 0 a 999.")]
    [InlineData(1000, 1, "A série da NF-e vai de 0 a 999.")]
    [InlineData(1, 0, "O próximo número da NF-e vai de 1 a 999.999.999.")]
    public async Task Save_validates_series_and_next_number(int series, int next, string expected)
    {
        var db = await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).SaveAsync("01", NfeEnvironment.Homologation, series, One(series, next), "tester"));

        Assert.Equal(expected, ex.Message);
    }

    private static async Task SeedDocumentAsync(UnitOfWork db, int series, bool thirdParty = false)
    {
        if (thirdParty)
        {
            db.Context.PurchaseInvoices.Add(new PurchaseInvoice
            {
                Key = Guid.NewGuid(), BranchCode = "01", CardCode = "C1", CardName = "X", IssuerType = DocumentIssuerType.ThirdParty,
                TaxDocumentSeries = series.ToString(), NfeEnvironment = NfeEnvironment.Homologation,
            });
        }
        else
        {
            db.Context.SalesInvoices.Add(new SalesInvoice
            {
                Key = Guid.NewGuid(), BranchCode = "01", CardCode = "C1", CardName = "X", TaxDocumentSeries = series.ToString(),
                NfeStatus = NfeStatus.Rejected, NfeEnvironment = NfeEnvironment.Homologation,
            });
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Save_refuses_removing_a_series_used_by_a_rejected_document()
    {
        var db = await SeedAsync();
        var service = Service(db);
        await service.SaveAsync("01", NfeEnvironment.Homologation, 1, [new(1, "Venda", 1), new(300, "Remessa", 1)], "tester");
        await SeedDocumentAsync(db, 300);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.SaveAsync("01", NfeEnvironment.Homologation, 1, One(), "tester"));

        Assert.Equal("A série 300 já foi usada em NF-e e não pode ser excluída.", ex.Message);
    }

    [Fact]
    public async Task Save_allows_removing_a_series_only_seen_on_third_party_entries()
    {
        var db = await SeedAsync();
        var service = Service(db);
        await service.SaveAsync("01", NfeEnvironment.Homologation, 1, [new(1, "Venda", 1), new(300, "Remessa", 1)], "tester");
        await SeedDocumentAsync(db, 300, thirdParty: true);

        var model = await service.SaveAsync("01", NfeEnvironment.Homologation, 1, One(), "tester");

        Assert.Equal([1], model.SeriesList.Select(x => x.Series));
    }

    [Fact]
    public async Task Get_marks_used_series()
    {
        var db = await SeedAsync();
        var service = Service(db);
        await service.SaveAsync("01", NfeEnvironment.Homologation, 1, [new(1, "Venda", 1), new(300, "Remessa", 1)], "tester");
        await SeedDocumentAsync(db, 300);

        var model = await service.GetAsync("01");

        Assert.Equal([false, true], model.SeriesList.Select(x => x.IsUsed));
    }

    [Fact]
    public async Task Upload_certificate_on_new_settings_creates_the_default_series()
    {
        var db = await SeedAsync();
        var service = Service(db);

        await service.UploadCertificateAsync("01", TestCertificates.CreatePfx(), TestCertificates.Password, "tester");
        var model = await service.GetAsync("01");

        Assert.Equal(1, model.DefaultSeries);
        var row = Assert.Single(model.SeriesList);
        Assert.Equal((1, "Padrão", 1), (row.Series, row.Description, row.NextNumber));
    }

    [Fact]
    public async Task Upload_stores_the_certificate_and_never_returns_the_password()
    {
        var db = await SeedAsync();

        var model = await Service(db).UploadCertificateAsync("01", TestCertificates.CreatePfx(), TestCertificates.Password, "tester");

        Assert.True(model.HasCertificate);
        Assert.Equal("12345678000195", model.CertificateTaxId);
        var saved = await db.Context.BranchNfeSettings.AsNoTracking().Include(s => s.SeriesList).SingleAsync();
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
        await SaveOne(Service(db), NfeEnvironment.Homologation, 1, 1);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service(db).TestConnectionAsync("01"));

        Assert.Contains("certificado", ex.Message);
    }
}
