using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.Dimensions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Dimensions;

/// <summary>Padrões de dimensão na natureza (cinco dimensões + conta) e no parceiro (dimensão de papel Parceiro).</summary>
public class UsagePartnerDimensionDefaultsTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private static IConfiguration Config() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Erp"] = "STANDALONE" }).Build();

    private UsageService Usages() =>
        new(_db, NullLogger<UsageService>.Instance, new DimensionCodeValidator(_db, Config()));

    private BusinessPartnerService Partners() =>
        new(_db, NullLogger<BusinessPartnerService>.Instance, new FakeStringLocalizer<Resource>(),
            new FakeDocNumberSequenceService(), Config(), new DimensionCodeValidator(_db, Config()));

    [Fact]
    public async Task Usage_saves_five_defaults()
    {
        await DimensionTestData.SeedDimensions(_db);
        foreach (var n in new[] { 2, 3, 4, 5 })
            _db.Context.AccountingDimensions.Single(x => x.Number == n).Active = true;
        for (var n = 1; n <= 5; n++)
            _db.Context.CostCenters.Add(new CostCenter { Code = $"CC{n}", Name = $"CC{n}", DimensionNumber = n });
        _db.Context.LedgerAccounts.Add(new LedgerAccount { Code = "1.1", Name = "Conta", AllowsPosting = true });
        await _db.SaveChangesAsync();

        var created = await Usages().CreateAsync(new UsageModel
        {
            Name = "Venda", CfopOutgoingInState = "5101", CfopOutgoingOutState = "6101", RequiresQuantity = true,
            Dim1Code = "cc1", Dim2Code = "CC2", Dim3Code = "CC3", Dim4Code = "CC4", Dim5Code = "CC5", LedgerAccountCode = "1.1",
        });

        var stored = _db.Context.Usages.Single(x => x.Code == created.Code);
        Assert.Equal("CC1", stored.Dim1Code);
        Assert.Equal("CC5", stored.Dim5Code);
        Assert.Equal("1.1", stored.LedgerAccountCode);

        created.Dim3Code = "ZZZ";
        await Assert.ThrowsAsync<DefaultException>(() => Usages().UpdateAsync(created.Code, created));
    }

    [Fact]
    public async Task Partner_default_requires_partner_role_dimension()
    {
        await DimensionTestData.SeedDimensions(_db);
        _db.Context.CostCenters.Add(new CostCenter { Code = "PRT", Name = "Parceiro", DimensionNumber = 1 });
        await _db.SaveChangesAsync();

        var model = new BusinessPartnerModel { CardCode = "C90001", CardName = "CLIENTE", CardType = "C", DefaultDimensionCode = "prt" };
        var error = await Assert.ThrowsAsync<DefaultException>(() => Partners().CreateAsync(model));
        Assert.Equal("Nenhuma dimensão tem o papel Parceiro.", error.Message);

        _db.Context.AccountingDimensions.Single(x => x.Number == 1).Role = DimensionRole.Partner;
        await _db.SaveChangesAsync();

        await Partners().CreateAsync(model);
        Assert.Equal("PRT", _db.Context.BusinessPartners.Single(x => x.CardCode == model.CardCode).DefaultDimensionCode);

        model.DefaultDimensionCode = "ZZZ";
        await Assert.ThrowsAsync<DefaultException>(() => Partners().UpdateAsync(model.CardCode, model));
    }
}
