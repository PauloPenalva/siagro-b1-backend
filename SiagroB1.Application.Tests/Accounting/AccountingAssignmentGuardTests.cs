using SiagroB1.Application.Services.Accounting;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.Accounting.AccountingTestData;

namespace SiagroB1.Application.Tests.Accounting;

/// <summary>Obrigatoriedade de CC/Item pelas flags da conta contábil (CT1_CCOBRG/ACCUST/ITOBRG/ACITEM).</summary>
public class AccountingAssignmentGuardTests
{
    private static async Task<IUnitOfWork> NewDb()
    {
        var db = TestDb.CreateUnitOfWork();
        await SeedAsync(db);
        db.Context.LedgerAccounts.AddRange(
            new LedgerAccount { Code = "FREE", Name = "Livre" },
            new LedgerAccount { Code = "REQCC", Name = "Exige CC", RequiresCostCenter = true },
            new LedgerAccount { Code = "NOCC", Name = "Não aceita CC", AcceptsCostCenter = false },
            new LedgerAccount { Code = "REQIT", Name = "Exige item", RequiresAccountingItem = true },
            new LedgerAccount { Code = "NOIT", Name = "Não aceita item", AcceptsAccountingItem = false });
        await db.SaveChangesAsync();
        // Antes de hoje todas as linhas têm CC; a partir de amanhã uma linha fica só com item.
        var rule = Rule("RCC", Today.AddDays(-10), ("CC01", "IT01", 50), ("CC02", "IT02", 50));
        rule.Versions.Add(Version(Today.AddDays(1), ("CC01", null, 50), (null, "IT02", 50)));
        await AddRuleAsync(db, rule);
        await AddRuleAsync(db, Rule("RIT", Today.AddDays(-10), (null, "IT01", 100)));
        return db;
    }

    private static AccountingAssignmentGuard Guard(IUnitOfWork db, string erp = "STANDALONE") => new(db, Config(erp));

    private static AccountingAssignmentSnapshot Row(string? ledger, string? center = null, string? item = null, string? rule = null) =>
        new() { LedgerAccountCode = ledger, CostCenterCode = center, AccountingItemCode = item, DistributionRuleCode = rule };

    private static async Task<string> Throws(Func<Task> act) =>
        (await Assert.ThrowsAsync<DefaultException>(act)).Message;

    [Fact]
    public async Task Account_requiring_cost_center_rejects_row_without_it()
    {
        var g = Guard(await NewDb());

        Assert.Equal("A conta REQCC exige centro de custo (linha 1 (X)).",
            await Throws(() => g.EnsureAsync([Row("REQCC")], i => $"{i + 1} (X)", Today)));
        Assert.Equal("A conta REQCC exige centro de custo.",
            await Throws(() => g.EnsureAsync([Row("REQCC")], null, Today)));
        await g.EnsureAsync([Row("REQCC", center: "CC01")], null, Today);
    }

    [Fact]
    public async Task Second_row_failure_uses_second_row_label()
    {
        var g = Guard(await NewDb());

        Assert.Equal("A conta REQCC exige centro de custo (linha 2).",
            await Throws(() => g.EnsureAsync([Row("REQCC", "CC01"), Row("REQCC", item: "IT01")], i => $"{i + 1}", Today)));
    }

    [Fact]
    public async Task Rule_satisfies_required_cost_center_only_if_every_line_has_it()
    {
        // Review Focus 2: vale a versão vigente na data do documento.
        var g = Guard(await NewDb());

        await g.EnsureAsync([Row("REQCC", rule: "RCC")], null, Today);
        Assert.Equal("A conta REQCC exige centro de custo.",
            await Throws(() => g.EnsureAsync([Row("REQCC", rule: "RCC")], null, Today.AddDays(1))));
        Assert.Equal("A conta REQCC exige centro de custo.",
            await Throws(() => g.EnsureAsync([Row("REQCC", rule: "RIT")], null, Today)));
    }

    [Fact]
    public async Task Account_not_accepting_cost_center_rejects_center_and_rule_with_center()
    {
        var g = Guard(await NewDb());

        Assert.Equal("A conta NOCC não aceita centro de custo (linha 1).",
            await Throws(() => g.EnsureAsync([Row("NOCC", "CC01")], i => $"{i + 1}", Today)));
        Assert.Equal("A conta NOCC não aceita centro de custo.",
            await Throws(() => g.EnsureAsync([Row("NOCC", rule: "RCC")], null, Today)));
        await g.EnsureAsync([Row("NOCC", rule: "RIT"), Row("NOCC", item: "IT01")], null, Today);
    }

    [Fact]
    public async Task Account_requiring_item_rejects_row_without_it()
    {
        var g = Guard(await NewDb());

        Assert.Equal("A conta REQIT exige item contábil.",
            await Throws(() => g.EnsureAsync([Row("REQIT", "CC01")], null, Today)));
        await g.EnsureAsync([Row("REQIT", item: "IT01"), Row("REQIT", rule: "RIT"), Row("REQIT", rule: "RCC")], null, Today);
        Assert.Equal("A conta REQIT exige item contábil.",
            await Throws(() => g.EnsureAsync([Row("REQIT", rule: "RCC")], null, Today.AddDays(1))));
    }

    [Fact]
    public async Task Account_not_accepting_item_rejects_item()
    {
        var g = Guard(await NewDb());

        Assert.Equal("A conta NOIT não aceita item contábil (linha 1).",
            await Throws(() => g.EnsureAsync([Row("NOIT", item: "IT01")], i => $"{i + 1}", Today)));
        Assert.Equal("A conta NOIT não aceita item contábil.",
            await Throws(() => g.EnsureAsync([Row("NOIT", rule: "RIT")], null, Today)));
        await g.EnsureAsync([Row("NOIT", "CC01")], null, Today);
    }

    [Fact]
    public async Task Row_without_account_is_skipped()
    {
        var g = Guard(await NewDb());

        await g.EnsureAsync([Row(null), Row(" ", "CC01")], i => $"{i + 1}", Today);
    }

    [Fact]
    public async Task Free_account_accepts_anything()
    {
        var g = Guard(await NewDb());

        await g.EnsureAsync([Row("FREE"), Row("FREE", "CC01", "IT01"), Row("FREE", rule: "RCC")], null, Today);
    }

    [Fact]
    public async Task Noop_in_sapb1()
    {
        var g = Guard(await NewDb(), "SAPB1");

        await g.EnsureAsync([Row("REQCC")], null, Today);
    }
}
