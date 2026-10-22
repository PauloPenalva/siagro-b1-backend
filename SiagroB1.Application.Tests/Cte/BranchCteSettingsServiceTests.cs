using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Cte;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Cte;

/// <summary>Configuração do CT-e por filial: séries, ambiente e requisitos para emitir.</summary>
public class BranchCteSettingsServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private BranchCteSettingsService Service(string erp = "STANDALONE") =>
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

    private static BranchCteSettingsModel Model(int nextNumber = 1, NfeEnvironment environment = NfeEnvironment.Homologation,
        bool enabled = false) => new()
    {
        Enabled = enabled,
        Environment = environment,
        DefaultSeries = 1,
        DefaultQuantityUnit = "00",
        SeriesList = [new BranchCteSeriesModel { Series = 1, Description = "Padrão", NextNumber = nextNumber }],
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
        Assert.Equal("Para emitir CT-e, cadastre o certificado (Configuração da NF-e) e o RNTRC (Filial → Transportadora).", ex.Message);

        await SeedCertificateAndRntrcAsync();

        var saved = await Service().SaveAsync("01", Model(enabled: true), "tester");
        Assert.True(saved.Enabled);
    }

    [Fact]
    public async Task Default_quantity_unit_is_m3()
    {
        await SeedBranchAsync();

        Assert.Equal("00", (await Service().GetAsync("01")).DefaultQuantityUnit);
        Assert.Equal("00", new BranchCteSettings { BranchCode = "01" }.DefaultQuantityUnit);
    }

    [Fact]
    public async Task Rejected_in_sapb1()
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service("SAPB1").GetAsync("01"));

        Assert.Equal("O CT-e só existe no modo STANDALONE.", ex.Message);
    }

    [Fact]
    public async Task Save_refuses_to_delete_a_used_series()
    {
        await SeedBranchAsync();
        await Service().SaveAsync("01", new BranchCteSettingsModel
        {
            DefaultSeries = 1,
            SeriesList =
            [
                new() { Series = 1, Description = "A", NextNumber = 1 },
                new() { Series = 2, Description = "B", NextNumber = 7 },
            ],
        }, "tester");
        _db.Context.TransportDocuments.Add(new TransportDocument { BranchCode = "01", Series = 2, Number = 6 });
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service().SaveAsync("01", Model(), "tester"));

        Assert.Contains("não pode ser excluída", ex.Message);
    }

    [Fact]
    public async Task Save_allows_deleting_a_series_whose_counter_moved_but_has_no_numbered_document()
    {
        await SeedBranchAsync();
        await Service().SaveAsync("01", new BranchCteSettingsModel
        {
            DefaultSeries = 1,
            SeriesList =
            [
                new() { Series = 1, Description = "A", NextNumber = 1 },
                new() { Series = 2, Description = "B", NextNumber = 7 },
            ],
        }, "tester");
        _db.Context.TransportDocuments.Add(new TransportDocument { BranchCode = "01", Series = 2, Number = null });
        await _db.SaveChangesAsync();

        var saved = await Service().SaveAsync("01", Model(), "tester");

        Assert.Single(saved.SeriesList);
    }
}
