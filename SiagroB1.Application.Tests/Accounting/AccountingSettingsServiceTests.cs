using SiagroB1.Application.Services.Accounting;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Accounting;

public class AccountingSettingsServiceTests
{
    private static async Task<IUnitOfWork> Db()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.LedgerAccounts.AddRange(
            new LedgerAccount { Code = "2.4.01", Name = "Resultado", Type = LedgerAccountType.Equity },
            new LedgerAccount { Code = "2.4", Name = "PL", Type = LedgerAccountType.Equity, AllowsPosting = false },
            new LedgerAccount { Code = "3.1.01", Name = "Receita", Type = LedgerAccountType.Revenue });
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Get_returns_defaults_when_never_saved()
    {
        var s = await new AccountingSettingsService(await Db()).GetAsync();
        Assert.Null(s.CutoverDate);
        Assert.Equal(AccountingClosingFrequency.Monthly, s.ClosingFrequency);
    }

    [Fact]
    public async Task Save_persists_and_overwrites()
    {
        var db = await Db();
        await new AccountingSettingsService(db).SaveAsync(new AccountingSettings
        {
            CutoverDate = new DateTime(2026, 1, 1), DefaultResultClosingAccountCode = "2.4.01",
            ClosingFrequency = AccountingClosingFrequency.Yearly,
        }, "admin");
        await new AccountingSettingsService(db).SaveAsync(new AccountingSettings { CutoverDate = new DateTime(2026, 7, 1) }, "joao");

        var s = await new AccountingSettingsService(db).GetAsync();
        Assert.Equal(new DateTime(2026, 7, 1), s.CutoverDate);
        Assert.Null(s.DefaultResultClosingAccountCode);
        Assert.Equal("joao", s.UpdatedBy);
        Assert.Equal(1, db.Context.AccountingSettings.Count());
    }

    [Theory]
    [InlineData("2.4")]
    [InlineData("3.1.01")]
    [InlineData("X")]
    public async Task Rejects_closing_account_not_analytic_equity_or_liability(string code)
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() => new AccountingSettingsService(Db().Result)
            .SaveAsync(new AccountingSettings { DefaultResultClosingAccountCode = code }, "admin"));
        Assert.Equal($"A conta de apuração {code} precisa ser analítica, de patrimônio líquido ou passivo.", ex.Message);
    }
}
