using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.Accounting;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.Accounting.AccountingTestData;

namespace SiagroB1.Application.Tests.Accounting;

/// <summary>Padrões de amarração contábil na natureza e no parceiro.</summary>
public class UsagePartnerAccountingDefaultsTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private UsageService Usages() =>
        new(_db, NullLogger<UsageService>.Instance, new AccountingAssignmentValidator(_db, Config()));

    private BusinessPartnerService Partners() =>
        new(_db, NullLogger<BusinessPartnerService>.Instance, new FakeStringLocalizer<Resource>(),
            new FakeDocNumberSequenceService(), Config(), new AccountingAssignmentValidator(_db, Config()));

    [Fact]
    public async Task Usage_saves_defaults_and_rejects_new_invalid_code()
    {
        await SeedAsync(_db);
        _db.Context.LedgerAccounts.Add(new LedgerAccount { Code = "1.1", Name = "Conta", AllowsPosting = true });
        await _db.SaveChangesAsync();

        var created = await Usages().CreateAsync(new UsageModel
        {
            Name = "Venda", CfopOutgoingInState = "5101", CfopOutgoingOutState = "6101", RequiresQuantity = true,
            CostCenterCode = "cc01", AccountingItemCode = "IT01", LedgerAccountCode = "1.1",
        });

        var stored = _db.Context.Usages.Single(x => x.Code == created.Code);
        Assert.Equal("CC01", stored.CostCenterCode);
        Assert.Equal("IT01", stored.AccountingItemCode);
        Assert.Equal("1.1", stored.LedgerAccountCode);

        created.AccountingItemCode = "ISYN";
        await Assert.ThrowsAsync<DefaultException>(() => Usages().UpdateAsync(created.Code, created));
    }

    [Fact]
    public async Task Partner_default_must_be_analytic_and_active()
    {
        await SeedAsync(_db);
        var model = new BusinessPartnerModel { CardCode = "C90001", CardName = "CLIENTE", CardType = "C", DefaultCostCenterCode = "SYN" };
        await Assert.ThrowsAsync<DefaultException>(() => Partners().CreateAsync(model));

        model.DefaultCostCenterCode = "cc01";
        model.DefaultAccountingItemCode = "it01";
        await Partners().CreateAsync(model);
        var stored = _db.Context.BusinessPartners.Single(x => x.CardCode == model.CardCode);
        Assert.Equal("CC01", stored.DefaultCostCenterCode);
        Assert.Equal("IT01", stored.DefaultAccountingItemCode);

        model.DefaultAccountingItemCode = "IOLD";
        await Assert.ThrowsAsync<DefaultException>(() => Partners().UpdateAsync(model.CardCode, model));
    }

    [Fact]
    public async Task Unchanged_partner_default_survives_blocking()
    {
        await SeedAsync(_db);
        var model = new BusinessPartnerModel { CardCode = "C90001", CardName = "CLIENTE", CardType = "C", DefaultCostCenterCode = "CC01" };
        await Partners().CreateAsync(model);

        _db.Context.CostCenters.Single(x => x.Code == "CC01").Inactive = true;
        await _db.SaveChangesAsync();

        model.CardName = "NOVO NOME";
        await Partners().UpdateAsync(model.CardCode, model);
        Assert.Equal("NOVO NOME", _db.Context.BusinessPartners.Single(x => x.CardCode == "C90001").CardName);

        model.DefaultCostCenterCode = "OLD";
        await Assert.ThrowsAsync<DefaultException>(() => Partners().UpdateAsync(model.CardCode, model));
    }
}
