using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Mdfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Mdfe;

/// <summary>Configuração do MDF-e por filial: séries, ambiente e requisitos para emitir.</summary>
public class BranchMdfeSettingsServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private BranchMdfeSettingsService Service(string erp = "STANDALONE") =>
        new(_db, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Erp"] = erp }).Build());

    private async Task SeedBranchAsync()
    {
        _db.Context.Branchs.Add(new Branch
        {
            Code = "01", BranchName = "CEAGUI", ShortName = "CEAGUI", TaxId = "12345678000195", StateCode = "SP",
        });
        await _db.SaveChangesAsync();
    }

    private async Task SeedCertificateAndRntrcAsync()
    {
        _db.Context.BranchNfeSettings.Add(new BranchNfeSettings { BranchCode = "01", CertificatePfx = [1, 2, 3] });
        _db.Context.BranchTransportSettings.Add(new BranchTransportSettings { BranchCode = "01", CompanyRntrc = "11382119" });
        await _db.SaveChangesAsync();
    }

    private static BranchMdfeSettingsModel Model(int nextNumber = 1, NfeEnvironment environment = NfeEnvironment.Homologation,
        bool enabled = false) => new()
    {
        Enabled = enabled,
        Environment = environment,
        DefaultSeries = 1,
                SeriesList = [new BranchMdfeSeriesModel { Series = 1, Description = "Padrão", NextNumber = nextNumber }],
    };

    [Fact]
    public async Task Save_rejects_number_going_back_in_same_environment()
    {
        await SeedBranchAsync();
        await Service().SaveAsync("01", Model(nextNumber: 50), "tester");

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service().SaveAsync("01", Model(nextNumber: 10), "tester"));

        Assert.Contains("não pode voltar", ex.Message);
    }

    [Fact]
    public async Task Save_allows_restart_when_environment_changes()
    {
        await SeedBranchAsync();
        await Service().SaveAsync("01", Model(nextNumber: 50), "tester");

        var saved = await Service().SaveAsync("01", Model(nextNumber: 1, environment: NfeEnvironment.Production), "tester");

        Assert.Equal(NfeEnvironment.Production, saved.Environment);
        Assert.Equal(1, saved.SeriesList.Single().NextNumber);
    }

    [Fact]
    public async Task Enabling_requires_certificate_and_rntrc()
    {
        await SeedBranchAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service().SaveAsync("01", Model(enabled: true), "tester"));
        Assert.Equal("Para emitir MDF-e, cadastre o certificado (Configuração da NF-e) e o RNTRC (Filial → Transportadora).", ex.Message);

        await SeedCertificateAndRntrcAsync();

        var saved = await Service().SaveAsync("01", Model(enabled: true), "tester");
        Assert.True(saved.Enabled);
    }

    [Fact]
    public async Task Defaults_are_bulk_liquid_and_ciot_not_required()
    {
        await SeedBranchAsync();

        var model = await Service().GetAsync("01");

        Assert.Equal("02", model.DefaultCargoType);
        Assert.False(model.RequiresCiot);
        Assert.Equal("02", new BranchMdfeSettings { BranchCode = "01" }.DefaultCargoType);
        Assert.False(new BranchMdfeSettings { BranchCode = "01" }.RequiresCiot);
    }

    [Fact]
    public async Task Rejected_in_sapb1()
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service("SAPB1").GetAsync("01"));

        Assert.Equal("O MDF-e só existe no modo STANDALONE.", ex.Message);
    }

    [Fact]
    public async Task Series_is_used_only_when_a_numbered_manifest_exists()
    {
        await SeedBranchAsync();
        var two = Model();
        two.SeriesList.Add(new BranchMdfeSeriesModel { Series = 2, Description = "Dois", NextNumber = 5 });
        two.SeriesList.Add(new BranchMdfeSeriesModel { Series = 3, Description = "Três", NextNumber = 1 });
        await Service().SaveAsync("01", two, "tester");
        _db.Context.TransportManifests.Add(new TransportManifest { BranchCode = "01", Series = 3, Number = 1 });
        _db.Context.TransportManifests.Add(new TransportManifest { BranchCode = "01", Series = 1 });
        await _db.SaveChangesAsync();

        var model = await Service().GetAsync("01");
        Assert.Equal([false, false, true], model.SeriesList.Select(x => x.IsUsed));

        // Série 2: contador andou mas nenhum MDF-e numerado → pode sair. Série 3 tem MDF-e nº 1 → bloqueada.
        var withoutTwo = Model();
        withoutTwo.SeriesList.Add(new BranchMdfeSeriesModel { Series = 3, Description = "Três", NextNumber = 1 });
        await Service().SaveAsync("01", withoutTwo, "tester");

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service().SaveAsync("01", Model(), "tester"));
        Assert.Equal("A série 3 já foi usada em MDF-e e não pode ser excluída.", ex.Message);
    }
}
