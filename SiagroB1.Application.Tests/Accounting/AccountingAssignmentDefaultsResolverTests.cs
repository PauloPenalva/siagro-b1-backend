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
            new LedgerAccount { Code = "1.2", Name = "Natureza" },
            new LedgerAccount { Code = "H.1", Name = "Cabeçalho" },
            new LedgerAccount { Code = "X.0", Name = "Inativa", Inactive = true });
        await db.SaveChangesAsync();
        return db;
    }

    private static async Task<int> AddUsage(IUnitOfWork db, string? center = null, string? item = null,
        string? rule = null, string? ledger = null)
    {
        var u = new Usage
        {
            Name = "Natureza", CostCenterCode = center, AccountingItemCode = item, DistributionRuleCode = rule,
            LedgerAccountCode = ledger,
        };
        db.Context.Usages.Add(u);
        await db.SaveChangesAsync();
        return u.Code;
    }

    private static async Task AddPartner(IUnitOfWork db, string? center, string? item = null)
    {
        db.Context.BusinessPartners.Add(new BusinessPartner
        {
            CardCode = "P1", CardName = "Parceiro", DefaultCostCenterCode = center, DefaultAccountingItemCode = item,
        });
        await db.SaveChangesAsync();
    }

    private static AccountingAssignmentDefaultsResolver Resolver(IUnitOfWork db, string erp = "STANDALONE") => new(db, Config(erp));

    [Fact]
    public async Task Header_wins_over_partner_and_usage()
    {
        var db = await NewDb();
        await AddPartner(db, "CC02", "IT02");
        var usage = await AddUsage(db, "CC01", "IT01", ledger: "1.2");
        var target = new AccountingAssignmentSnapshot();

        await Resolver(db).ApplyAsync(target,
            new AccountingAssignmentSnapshot { CostCenterCode = "CC01", LedgerAccountCode = "H.1" }, "P1", usage, Today);

        Assert.Equal("CC01", target.CostCenterCode);
        Assert.Equal("IT02", target.AccountingItemCode);
        Assert.Equal("H.1", target.LedgerAccountCode);
    }

    [Fact]
    public async Task Header_rule_beats_partner_cost_center()
    {
        var db = await NewDb();
        await AddPartner(db, "CC02", "IT02");
        var target = new AccountingAssignmentSnapshot();

        await Resolver(db).ApplyAsync(target,
            new AccountingAssignmentSnapshot { DistributionRuleCode = "R01" }, "P1", null, Today);

        Assert.Equal("R01", target.DistributionRuleCode);
        Assert.Null(target.CostCenterCode);
        Assert.Null(target.AccountingItemCode);
    }

    [Fact]
    public async Task Partner_cost_center_beats_usage_rule()
    {
        var db = await NewDb();
        await AddPartner(db, "CC02");
        var usage = await AddUsage(db, item: "IT01", rule: "R01");
        var target = new AccountingAssignmentSnapshot();

        await Resolver(db).ApplyAsync(target, null, "P1", usage, Today);

        Assert.Null(target.DistributionRuleCode);
        Assert.Equal("CC02", target.CostCenterCode);
        Assert.Equal("IT01", target.AccountingItemCode);
    }

    [Fact]
    public async Task Partner_wins_over_usage()
    {
        var db = await NewDb();
        await AddPartner(db, "CC02");
        var usage = await AddUsage(db, "CC01", "IT01", ledger: "1.2");
        var target = new AccountingAssignmentSnapshot();

        await Resolver(db).ApplyAsync(target, null, "P1", usage, Today);

        Assert.Equal("CC02", target.CostCenterCode);
        Assert.Equal("IT01", target.AccountingItemCode);
        Assert.Equal("1.2", target.LedgerAccountCode);
    }

    [Fact]
    public async Task Typed_value_is_never_overwritten()
    {
        var db = await NewDb();
        var usage = await AddUsage(db, "CC01", ledger: "1.2");
        var target = new AccountingAssignmentSnapshot { CostCenterCode = "CC02", LedgerAccountCode = "H.1" };

        await Resolver(db).ApplyAsync(target, null, null, usage, Today);

        Assert.Equal("CC02", target.CostCenterCode);
        Assert.Equal("H.1", target.LedgerAccountCode);
    }

    [Fact]
    public async Task Invalid_candidates_are_skipped()
    {
        var db = await NewDb();
        await AddPartner(db, "OLD", "ISYN");
        var usage = await AddUsage(db, "CC01", "IT01", ledger: "X.0");
        var target = new AccountingAssignmentSnapshot();

        await Resolver(db).ApplyAsync(target, new AccountingAssignmentSnapshot { CostCenterCode = "EXP" }, "P1", usage, Today);

        Assert.Equal("CC01", target.CostCenterCode);
        Assert.Equal("IT01", target.AccountingItemCode);
        Assert.Null(target.LedgerAccountCode);
    }

    [Fact]
    public async Task Rule_from_usage_when_nothing_else()
    {
        var db = await NewDb();
        var usage = await AddUsage(db, rule: "R01");
        var target = new AccountingAssignmentSnapshot();

        await Resolver(db).ApplyAsync(target, null, null, usage, Today);

        Assert.Equal("R01", target.DistributionRuleCode);
    }

    [Fact]
    public async Task Rule_target_never_gets_center_or_item()
    {
        var db = await NewDb();
        await AddPartner(db, "CC02", "IT02");
        var usage = await AddUsage(db, "CC01", "IT01");
        var target = new AccountingAssignmentSnapshot { DistributionRuleCode = "R01" };

        await Resolver(db).ApplyAsync(target, null, "P1", usage, Today);

        Assert.Null(target.CostCenterCode);
        Assert.Null(target.AccountingItemCode);
    }

    [Fact]
    public async Task Center_target_never_gets_rule()
    {
        var db = await NewDb();
        var usage = await AddUsage(db, rule: "R01");
        var target = new AccountingAssignmentSnapshot { AccountingItemCode = "IT01" };

        await Resolver(db).ApplyAsync(target, null, null, usage, Today);

        Assert.Null(target.DistributionRuleCode);
    }

    [Fact]
    public async Task Noop_in_sapb1()
    {
        var db = await NewDb();
        var usage = await AddUsage(db, "CC01");
        var target = new AccountingAssignmentSnapshot();

        await Resolver(db, "SAPB1").ApplyAsync(target, null, null, usage, Today);

        Assert.Null(target.CostCenterCode);
    }
}
