using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.Accounting;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.Accounting.AccountingTestData;

namespace SiagroB1.Application.Tests.Accounting;

/// <summary>Conta contábil, CC e item contábil padrão do produto; conta contábil da conta financeira.</summary>
public class ItemAndFinancialAccountLedgerTests
{
    private static async Task<IUnitOfWork> NewDb()
    {
        var db = TestDb.CreateUnitOfWork();
        await SeedAsync(db);
        db.Context.LedgerAccounts.AddRange(
            new LedgerAccount { Code = "1.1", Name = "Analítica", AllowsPosting = true },
            new LedgerAccount { Code = "1", Name = "Sintética", AllowsPosting = false },
            new LedgerAccount { Code = "X.0", Name = "Inativa", Inactive = true, AllowsPosting = true });
        await db.SaveChangesAsync();
        return db;
    }

    private static ItemService Items(IUnitOfWork db, string erp = "STANDALONE") =>
        new(db, NullLogger<ItemService>.Instance, Config(erp), new FakeDocNumberSequenceService(),
            new AccountingAssignmentValidator(db, Config(erp)));

    private static ItemModel Product(string? ledger = null, string? cc = null, string? item = null) => new()
    {
        ItemCode = "SOJA", ItemName = "Soja", Enabled = "SIM",
        LedgerAccountCode = ledger, CostCenterCode = cc, AccountingItemCode = item,
    };

    [Fact]
    public async Task Item_saves_normalized_defaults()
    {
        var db = await NewDb();

        var saved = await Items(db).CreateAsync(Product(" 1.1 ", "cc01", "it01"));

        Assert.Equal("1.1", saved.LedgerAccountCode);
        var entity = await db.Context.Items.FindAsync("SOJA");
        Assert.Equal("CC01", entity!.CostCenterCode);
        Assert.Equal("IT01", entity.AccountingItemCode);
    }

    [Theory]
    [InlineData("1", null, null)]
    [InlineData("X.0", null, null)]
    [InlineData(null, "SYN", null)]
    [InlineData(null, "OLD", null)]
    [InlineData(null, null, "ISYN")]
    [InlineData(null, null, "NOPE")]
    public async Task Item_rejects_invalid_defaults(string? ledger, string? cc, string? item)
    {
        var db = await NewDb();

        await Assert.ThrowsAsync<DefaultException>(() => Items(db).CreateAsync(Product(ledger, cc, item)));
    }

    [Fact]
    public async Task Item_keeps_unchanged_value_that_became_invalid()
    {
        var db = await NewDb();
        await Items(db).CreateAsync(Product("1.1", "CC01"));
        (await db.Context.CostCenters.FindAsync("CC01"))!.Inactive = true;
        await db.SaveChangesAsync();

        var updated = await Items(db).UpdateAsync("SOJA", Product("1.1", "CC01"));

        Assert.Equal("CC01", updated!.CostCenterCode);
        await Assert.ThrowsAsync<DefaultException>(() => Items(db).UpdateAsync("SOJA", Product("1.1", "OLD")));
    }

    [Fact]
    public async Task Item_skips_validation_in_sapb1()
    {
        var db = await NewDb();

        var saved = await Items(db, "SAPB1").CreateAsync(Product("1", "SYN"));

        Assert.Equal("1", saved.LedgerAccountCode);
    }

    private static FinancialAccount Account(string? ledger) => new()
    {
        Code = "CX", Name = "Caixa", Type = FinancialAccountType.Cash, LedgerAccountCode = ledger,
    };

    [Fact]
    public async Task Financial_account_validates_ledger_on_create_and_change()
    {
        var db = await NewDb();
        var validator = new AccountingAssignmentValidator(db, Config());

        await Assert.ThrowsAsync<DefaultException>(() =>
            new FinancialAccountsCreateService(db, validator).ExecuteAsync(Account("1"), "u"));
        var created = await new FinancialAccountsCreateService(db, validator).ExecuteAsync(Account(" 1.1 "), "u");
        Assert.Equal("1.1", created.LedgerAccountCode);

        (await db.Context.LedgerAccounts.FindAsync("1.1"))!.Inactive = true;
        await db.SaveChangesAsync();
        var update = new FinancialAccountsUpdateService(db, validator);
        var kept = await update.ExecuteAsync("CX", a => a.Name = "Caixa geral");
        Assert.Equal("1.1", kept.LedgerAccountCode);
        await Assert.ThrowsAsync<DefaultException>(() => update.ExecuteAsync("CX", a => a.LedgerAccountCode = "X.0"));
    }

    [Fact]
    public async Task Financial_account_skips_validation_in_sapb1()
    {
        var db = await NewDb();

        var created = await new FinancialAccountsCreateService(db, new AccountingAssignmentValidator(db, Config("SAPB1")))
            .ExecuteAsync(Account("1"), "u");

        Assert.Equal("1", created.LedgerAccountCode);
    }
}
