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

/// <summary>Conta contábil e item contábil padrão do parceiro (a natureza não traz padrão contábil).</summary>
public class PartnerAccountingDefaultsTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private BusinessPartnerService Partners() =>
        new(_db, NullLogger<BusinessPartnerService>.Instance, new FakeStringLocalizer<Resource>(),
            new FakeDocNumberSequenceService(), Config(), new AccountingAssignmentValidator(_db, Config()));

    private async Task SeedLedgersAsync()
    {
        await SeedAsync(_db);
        _db.Context.LedgerAccounts.AddRange(
            new LedgerAccount { Code = "1.1", Name = "Clientes", AllowsPosting = true },
            new LedgerAccount { Code = "1", Name = "Ativo", AllowsPosting = false },
            new LedgerAccount { Code = "1.9", Name = "Inativa", AllowsPosting = true, Inactive = true });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Partner_defaults_are_normalized_and_validated()
    {
        await SeedLedgersAsync();
        var model = new BusinessPartnerModel { CardCode = "C90001", CardName = "CLIENTE", CardType = "C", DefaultAccountingItemCode = "ISYN" };
        await Assert.ThrowsAsync<DefaultException>(() => Partners().CreateAsync(model));

        model.DefaultAccountingItemCode = "it01";
        model.DefaultLedgerAccountCode = " 1.1 ";
        await Partners().CreateAsync(model);
        var stored = _db.Context.BusinessPartners.Single(x => x.CardCode == model.CardCode);
        Assert.Equal("IT01", stored.DefaultAccountingItemCode);
        Assert.Equal("1.1", stored.DefaultLedgerAccountCode);

        model.DefaultAccountingItemCode = "IOLD";
        await Assert.ThrowsAsync<DefaultException>(() => Partners().UpdateAsync(model.CardCode, model));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("1.9")]
    [InlineData("ZZZ")]
    public async Task Partner_ledger_must_be_active_and_allow_posting(string ledger)
    {
        await SeedLedgersAsync();
        var model = new BusinessPartnerModel { CardCode = "C90001", CardName = "CLIENTE", CardType = "C", DefaultLedgerAccountCode = ledger };
        await Assert.ThrowsAsync<DefaultException>(() => Partners().CreateAsync(model));
    }

    [Fact]
    public async Task Unchanged_partner_default_survives_inactivation()
    {
        await SeedLedgersAsync();
        var model = new BusinessPartnerModel
        {
            CardCode = "C90001", CardName = "CLIENTE", CardType = "C", DefaultLedgerAccountCode = "1.1", DefaultAccountingItemCode = "IT01",
        };
        await Partners().CreateAsync(model);

        _db.Context.LedgerAccounts.Single(x => x.Code == "1.1").Inactive = true;
        _db.Context.AccountingItems.Single(x => x.Code == "IT01").Inactive = true;
        await _db.SaveChangesAsync();

        model.CardName = "NOVO NOME";
        await Partners().UpdateAsync(model.CardCode, model);
        Assert.Equal("NOVO NOME", _db.Context.BusinessPartners.Single(x => x.CardCode == "C90001").CardName);

        model.DefaultLedgerAccountCode = "1.9";
        await Assert.ThrowsAsync<DefaultException>(() => Partners().UpdateAsync(model.CardCode, model));
    }
}
