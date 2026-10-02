using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Branches;

/// <summary>
/// Dados do emitente da NF-e na filial (só STANDALONE). O município manda no cUF e no cMun do
/// XML, então precisa ser da mesma UF da filial — senão a SEFAZ rejeita.
/// </summary>
public class BranchIssuerFieldsTests
{
    private static BranchService Service(UnitOfWork db, string erp) => new(db.Context, TaxTestServices.Config(erp));

    private static async Task<UnitOfWork> SeedAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Municipalities.AddRange(
            new Municipality { Code = "3522406", Name = "Itapeva", StateAbbreviation = "SP" },
            new Municipality { Code = "4106902", Name = "Curitiba", StateAbbreviation = "PR" });
        await db.SaveChangesAsync();
        return db;
    }

    private static Branch NewBranch(string? municipality = "3522406", string state = "SP") => new()
    {
        Code = "01", BranchName = "MATRIZ", ShortName = "MTZ", TaxId = "68583898000101", StateCode = state,
        LegalName = "CEAGUI CEREAIS LTDA", TradeName = "CEAGUI", StateRegistration = "371012345110",
        Street = "RODOVIA SP 258", StreetNumber = "KM 290", District = "ZONA RURAL",
        MunicipalityCode = municipality, ZipCode = "18400000", Phone = "1535261234",
    };

    [Fact]
    public async Task Issuer_fields_are_persisted()
    {
        var db = await SeedAsync();

        await Service(db, "STANDALONE").CreateAsync(NewBranch());

        var saved = await db.Context.Branchs.AsNoTracking().SingleAsync();
        Assert.Equal("CEAGUI CEREAIS LTDA", saved.LegalName);
        Assert.Equal("371012345110", saved.StateRegistration);
        Assert.Equal("3522406", saved.MunicipalityCode);
        Assert.Equal("18400000", saved.ZipCode);
    }

    [Fact]
    public async Task Standalone_rejects_municipality_from_another_state()
    {
        var db = await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db, "STANDALONE").CreateAsync(NewBranch(municipality: "4106902")));

        Assert.Contains("Curitiba", ex.Message);
        Assert.Contains("PR", ex.Message);
    }

    [Fact]
    public async Task Standalone_rejects_unknown_municipality()
    {
        var db = await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db, "STANDALONE").CreateAsync(NewBranch(municipality: "9999999")));

        Assert.Contains("9999999", ex.Message);
    }

    [Fact]
    public async Task Standalone_rejects_zip_code_without_eight_digits()
    {
        var db = await SeedAsync();
        var branch = NewBranch();
        branch.ZipCode = "18400-00";

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service(db, "STANDALONE").CreateAsync(branch));

        Assert.Contains("CEP", ex.Message);
    }

    [Fact]
    public async Task Sapb1_does_not_validate_issuer_fields()
    {
        var db = await SeedAsync();

        await Service(db, "SAPB1").CreateAsync(NewBranch(municipality: "4106902"));

        Assert.Equal(1, await db.Context.Branchs.CountAsync());
    }
}
