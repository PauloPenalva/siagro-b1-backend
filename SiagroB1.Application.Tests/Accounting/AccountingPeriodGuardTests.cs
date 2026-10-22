using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Accounting;

namespace SiagroB1.Application.Tests.Accounting;

public class AccountingPeriodGuardTests
{
    private static async Task<IUnitOfWork> Db(params (int Month, AccountingPeriodStatus Status)[] months)
    {
        var db = TestDb.CreateUnitOfWork();
        foreach (var (m, s) in months)
        {
            var start = new DateTime(2026, m, 1);
            db.Context.AccountingPeriods.Add(new AccountingPeriod
            {
                Year = 2026, Month = m, StartDate = start, EndDate = start.AddMonths(1).AddDays(-1), Status = s,
            });
        }
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Guard_allows_date_without_period()
    {
        var db = await Db();
        await new AccountingPeriodGuard(db).EnsureOpenAsync(new DateTime(2026, 5, 10));
    }

    [Fact]
    public async Task Guard_allows_open_period()
    {
        var db = await Db((5, AccountingPeriodStatus.Open));
        await new AccountingPeriodGuard(db).EnsureOpenAsync(new DateTime(2026, 5, 10));
    }

    [Fact]
    public async Task Guard_rejects_closed()
    {
        var db = await Db((5, AccountingPeriodStatus.Closed));
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            new AccountingPeriodGuard(db).EnsureOpenAsync(new DateTime(2026, 5, 31, 23, 0, 0)));
        Assert.Equal("Período contábil 05/2026 fechado.", ex.Message);
    }

    [Fact]
    public async Task Guard_rejects_blocked()
    {
        var db = await Db((5, AccountingPeriodStatus.Blocked));
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            new AccountingPeriodGuard(db).EnsureOpenAsync(new DateTime(2026, 5, 1)));
        Assert.Equal("Período contábil 05/2026 bloqueado.", ex.Message);
    }

    [Fact]
    public async Task FirstOpenDate_skips_closed_months()
    {
        var db = await Db((5, AccountingPeriodStatus.Closed), (6, AccountingPeriodStatus.Blocked), (7, AccountingPeriodStatus.Open));
        var guard = new AccountingPeriodGuard(db);
        Assert.Equal(new DateTime(2026, 7, 1), await guard.FirstOpenDateOnOrAfterAsync(new DateTime(2026, 5, 20)));
        Assert.Equal(new DateTime(2026, 7, 15), await guard.FirstOpenDateOnOrAfterAsync(new DateTime(2026, 7, 15)));
        Assert.Equal(new DateTime(2026, 4, 3), await guard.FirstOpenDateOnOrAfterAsync(new DateTime(2026, 4, 3)));
    }
}
