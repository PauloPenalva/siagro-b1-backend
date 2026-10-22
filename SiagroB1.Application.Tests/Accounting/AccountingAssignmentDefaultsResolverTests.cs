using SiagroB1.Application.Services.Accounting;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.Accounting.AccountingTestData;

namespace SiagroB1.Application.Tests.Accounting;

public class AccountingAssignmentDefaultsResolverTests
{
    private static async Task<IUnitOfWork> NewDb()
    {
        var db = TestDb.CreateUnitOfWork();
        await SeedAsync(db);
        await AddRuleAsync(db, Rule("R01", Today, ("CC01", null, 100)));
        db.Context.LedgerAccounts.AddRange(
            new LedgerAccount { Code = "1.2", Name = "Parceiro", AllowsPosting = true },
            new LedgerAccount { Code = "H.1", Name = "Cabeçalho", AllowsPosting = true },
            new LedgerAccount { Code = "X.0", Name = "Inativa", Inactive = true, AllowsPosting = true });
        await db.SaveChangesAsync();
        return db;
    }

    private static async Task AddPartner(IUnitOfWork db, string? item, string? ledger = null)
    {
        db.Context.BusinessPartners.Add(new BusinessPartner
        {
            CardCode = "P1", CardName = "Parceiro", DefaultAccountingItemCode = item, DefaultLedgerAccountCode = ledger,
        });
        await db.SaveChangesAsync();
    }

    private static AccountingAssignmentDefaultsResolver Resolver(IUnitOfWork db, string erp = "STANDALONE") => new(db, Config(erp));

    [Fact]
    public async Task Header_wins_over_partner()
    {
        var db = await NewDb();
        await AddPartner(db, "IT02", "1.2");
        var target = new AccountingAssignmentSnapshot();

        await Resolver(db).ApplyAsync(target,
            new AccountingAssignmentSnapshot { CostCenterCode = "CC01", LedgerAccountCode = "H.1" }, "P1", Today);

        Assert.Equal("CC01", target.CostCenterCode);
        Assert.Equal("IT02", target.AccountingItemCode);
        Assert.Equal("H.1", target.LedgerAccountCode);
    }

    [Fact]
    public async Task Partner_offers_item_and_ledger_account()
    {
        var db = await NewDb();
        await AddPartner(db, "IT02", "1.2");
        var target = new AccountingAssignmentSnapshot();

        await Resolver(db).ApplyAsync(target, null, "P1", Today);

        Assert.Null(target.CostCenterCode);
        Assert.Equal("IT02", target.AccountingItemCode);
        Assert.Equal("1.2", target.LedgerAccountCode);
    }

    [Fact]
    public async Task Header_rule_beats_partner_item()
    {
        var db = await NewDb();
        await AddPartner(db, "IT02");
        var target = new AccountingAssignmentSnapshot();

        await Resolver(db).ApplyAsync(target,
            new AccountingAssignmentSnapshot { DistributionRuleCode = "R01" }, "P1", Today);

        Assert.Equal("R01", target.DistributionRuleCode);
        Assert.Null(target.CostCenterCode);
        Assert.Null(target.AccountingItemCode);
    }

    [Fact]
    public async Task Typed_value_is_never_overwritten()
    {
        var db = await NewDb();
        await AddPartner(db, "IT02", "1.2");
        var target = new AccountingAssignmentSnapshot { AccountingItemCode = "IT01", LedgerAccountCode = "H.1" };

        await Resolver(db).ApplyAsync(target, null, "P1", Today);

        Assert.Equal("IT01", target.AccountingItemCode);
        Assert.Equal("H.1", target.LedgerAccountCode);
    }

    [Fact]
    public async Task Invalid_candidates_are_skipped()
    {
        var db = await NewDb();
        await AddPartner(db, "ISYN", "X.0");
        var target = new AccountingAssignmentSnapshot();

        await Resolver(db).ApplyAsync(target, new AccountingAssignmentSnapshot { CostCenterCode = "EXP" }, "P1", Today);

        Assert.Null(target.CostCenterCode);
        Assert.Null(target.AccountingItemCode);
        Assert.Null(target.LedgerAccountCode);
    }

    [Fact]
    public async Task Rule_target_never_gets_center_or_item()
    {
        var db = await NewDb();
        await AddPartner(db, "IT02");
        var target = new AccountingAssignmentSnapshot { DistributionRuleCode = "R01" };

        await Resolver(db).ApplyAsync(target, new AccountingAssignmentSnapshot { CostCenterCode = "CC01" }, "P1", Today);

        Assert.Null(target.CostCenterCode);
        Assert.Null(target.AccountingItemCode);
    }

    [Fact]
    public async Task Center_target_never_gets_rule()
    {
        var db = await NewDb();
        var target = new AccountingAssignmentSnapshot { AccountingItemCode = "IT01" };

        await Resolver(db).ApplyAsync(target, new AccountingAssignmentSnapshot { DistributionRuleCode = "R01" }, null, Today);

        Assert.Null(target.DistributionRuleCode);
    }

    [Fact]
    public async Task Noop_in_sapb1()
    {
        var db = await NewDb();
        await AddPartner(db, "IT02", "1.2");
        var target = new AccountingAssignmentSnapshot();

        await Resolver(db, "SAPB1").ApplyAsync(target, null, "P1", Today);

        Assert.Null(target.AccountingItemCode);
        Assert.Null(target.LedgerAccountCode);
    }
}
