using SiagroB1.Application.Services.Accounting;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.Accounting.AccountingTestData;

namespace SiagroB1.Application.Tests.Accounting;

public class AccountingAssignmentValidatorTests
{
    private static async Task<IUnitOfWork> NewDb()
    {
        var db = TestDb.CreateUnitOfWork();
        await SeedAsync(db);
        await AddRuleAsync(db, Rule("R01", Today, ("CC01", null, 100)));
        var inactive = Rule("R02", Today, ("CC01", null, 100));
        inactive.Inactive = true;
        await AddRuleAsync(db, inactive);
        db.Context.LedgerAccounts.AddRange(
            new LedgerAccount { Code = "1.1", Name = "Sintética", AllowsPosting = false },
            new LedgerAccount { Code = "1.2", Name = "Analítica" });
        await db.SaveChangesAsync();
        return db;
    }

    private static AccountingAssignmentValidator Validator(IUnitOfWork db, string erp = "STANDALONE") => new(db, Config(erp));

    private static async Task<string> Throws(Func<Task> act) =>
        (await Assert.ThrowsAsync<DefaultException>(act)).Message;

    [Fact]
    public async Task Accepts_analytic_active_center_item_and_rule()
    {
        var v = Validator(await NewDb());

        await v.NormalizeAndValidateAsync(new AccountingAssignmentSnapshot { CostCenterCode = "CC01", AccountingItemCode = "IT01" }, null, Today);
        await v.NormalizeAndValidateAsync(new AccountingAssignmentSnapshot { DistributionRuleCode = "R01" }, null, Today);
    }

    [Fact]
    public async Task Normalizes_lowercase_and_spaces()
    {
        var v = Validator(await NewDb());
        var f = new AccountingAssignmentSnapshot { CostCenterCode = " cc01 ", AccountingItemCode = "  ", LedgerAccountCode = " 1.2 " };

        await v.NormalizeAndValidateAsync(f, null, Today);

        Assert.Equal("CC01", f.CostCenterCode);
        Assert.Null(f.AccountingItemCode);
        Assert.Equal("1.2", f.LedgerAccountCode);
    }

    [Theory]
    [InlineData("SYN")]
    [InlineData("OLD")]
    [InlineData("ZZ")]
    [InlineData("FUT")]
    public async Task Rejects_invalid_new_center(string code)
    {
        var v = Validator(await NewDb());

        Assert.Equal($"O centro de custo {code} não existe, é sintético, está bloqueado ou fora da vigência.",
            await Throws(() => v.NormalizeAndValidateAsync(new AccountingAssignmentSnapshot { CostCenterCode = code }, null, Today)));
    }

    [Fact]
    public async Task Center_expired_before_document_date_is_rejected()
    {
        // Review Focus 4: EXP vence ontem; vale para documento de ontem, não para o de hoje.
        var v = Validator(await NewDb());

        await v.NormalizeAndValidateAsync(new AccountingAssignmentSnapshot { CostCenterCode = "EXP" }, null, Today.AddDays(-1));
        Assert.Equal("O centro de custo EXP não existe, é sintético, está bloqueado ou fora da vigência.",
            await Throws(() => v.NormalizeAndValidateAsync(new AccountingAssignmentSnapshot { CostCenterCode = "EXP" }, null, Today)));
    }

    [Theory]
    [InlineData("ISYN")]
    [InlineData("IOLD")]
    public async Task Rejects_invalid_new_item(string code)
    {
        var v = Validator(await NewDb());

        Assert.Equal($"O item contábil {code} não existe, é sintético, está bloqueado ou fora da vigência.",
            await Throws(() => v.NormalizeAndValidateAsync(new AccountingAssignmentSnapshot { AccountingItemCode = code }, null, Today)));
    }

    [Fact]
    public async Task Unchanged_blocked_or_synthetic_value_is_accepted()
    {
        // Review Focus 1: documento antigo continua editável; só valor novo é recusado.
        var v = Validator(await NewDb());

        await v.NormalizeAndValidateAsync(
            new AccountingAssignmentSnapshot { CostCenterCode = "old", AccountingItemCode = "ISYN" },
            new AccountingAssignmentSnapshot { CostCenterCode = "OLD", AccountingItemCode = "isyn" }, Today);
        await v.NormalizeAndValidateAsync(
            new AccountingAssignmentSnapshot { CostCenterCode = "SYN" },
            new AccountingAssignmentSnapshot { CostCenterCode = "SYN" }, Today);
    }

    [Fact]
    public async Task Rejects_missing_or_inactive_rule()
    {
        var v = Validator(await NewDb());

        foreach (var code in new[] { "R02", "ZZ" })
        {
            Assert.Equal($"O rateio {code} não existe ou está inativo.",
                await Throws(() => v.NormalizeAndValidateAsync(new AccountingAssignmentSnapshot { DistributionRuleCode = code }, null, Today)));
        }
    }

    [Fact]
    public async Task Rule_and_center_or_item_together_are_rejected()
    {
        // Review Focus 5.
        var v = Validator(await NewDb());
        const string message = "Informe o rateio ou o centro de custo/item contábil, não os dois.";

        Assert.Equal(message, await Throws(() => v.NormalizeAndValidateAsync(
            new AccountingAssignmentSnapshot { DistributionRuleCode = "R01", CostCenterCode = "CC01" }, null, Today)));
        Assert.Equal(message, await Throws(() => v.NormalizeAndValidateAsync(
            new AccountingAssignmentSnapshot { DistributionRuleCode = "R01", AccountingItemCode = "IT01" }, null, Today)));
    }

    [Fact]
    public async Task Rejects_non_posting_ledger_account()
    {
        var v = Validator(await NewDb());

        Assert.Equal("A conta contábil 1.1 não existe, está inativa ou não aceita lançamento.",
            await Throws(() => v.NormalizeAndValidateAsync(new AccountingAssignmentSnapshot { LedgerAccountCode = "1.1" }, null, Today)));
    }

    [Fact]
    public async Task Noop_in_sapb1()
    {
        var v = Validator(await NewDb(), "SAPB1");
        var f = new AccountingAssignmentSnapshot { CostCenterCode = " zzz ", DistributionRuleCode = "R01" };

        await v.NormalizeAndValidateAsync(f, null, Today);

        Assert.Equal(" zzz ", f.CostCenterCode);
    }
}
