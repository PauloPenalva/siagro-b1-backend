using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services.Accounting;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Accounting;

public class AccountingPeriodsServiceTests
{
    private sealed class RolePermissions(params string[] roles) : IUserPermissions
    {
        public Task<bool> HasAsync(string username, string permissionCode) => Task.FromResult(false);
        public Task<List<string>> GetAsync(string username) => Task.FromResult(new List<string>());
        public Task<bool> HasRoleAsync(string username, string roleCode) => Task.FromResult(roles.Contains(roleCode));
    }

    private static AccountingPeriodsService Service(IUnitOfWork db, bool admin = true) =>
        new(db, new RolePermissions(admin ? [Domain.Constants.Roles.Admin] : []));

    private static async Task<(IUnitOfWork Db, List<AccountingPeriod> Periods)> YearAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        var periods = await Service(db).GenerateYearAsync(2026, "admin");
        return (db, periods);
    }

    [Fact]
    public async Task GenerateYear_creates_twelve_open_months_and_is_idempotent()
    {
        var (db, periods) = await YearAsync();
        Assert.Equal(12, periods.Count);
        Assert.All(periods, p => Assert.Equal(AccountingPeriodStatus.Open, p.Status));
        Assert.Equal(new DateTime(2026, 2, 28), periods.Single(p => p.Month == 2).EndDate);

        await Service(db).GenerateYearAsync(2026, "admin");
        Assert.Equal(12, db.Context.AccountingPeriods.Count());
    }

    [Fact]
    public async Task Close_requires_previous_closed()
    {
        var (db, p) = await YearAsync();
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).SetStatusAsync(p.Single(x => x.Month == 2).Id, AccountingPeriodStatus.Closed, "admin", null));
        Assert.Equal("Feche antes o período 01/2026.", ex.Message);

        await Service(db).SetStatusAsync(p.Single(x => x.Month == 1).Id, AccountingPeriodStatus.Closed, "admin", null);
        var feb = await Service(db).SetStatusAsync(p.Single(x => x.Month == 2).Id, AccountingPeriodStatus.Closed, "admin", null);
        Assert.Equal(AccountingPeriodStatus.Closed, feb.Status);
        Assert.Equal("admin", feb.ClosedBy);
    }

    [Fact]
    public async Task Reopen_requires_reason()
    {
        var (db, p) = await YearAsync();
        var jan = p.Single(x => x.Month == 1).Id;
        await Service(db).SetStatusAsync(jan, AccountingPeriodStatus.Closed, "admin", null);
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).SetStatusAsync(jan, AccountingPeriodStatus.Open, "admin", " "));
        Assert.Equal("Informe o motivo da reabertura.", ex.Message);
    }

    [Fact]
    public async Task Reopen_requires_admin_role()
    {
        var (db, p) = await YearAsync();
        var jan = p.Single(x => x.Month == 1).Id;
        await Service(db).SetStatusAsync(jan, AccountingPeriodStatus.Closed, "admin", null);
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db, admin: false).SetStatusAsync(jan, AccountingPeriodStatus.Open, "joao", "ajuste"));
        Assert.Equal("Somente o papel ADMIN pode reabrir período contábil.", ex.Message);

        var reopened = await Service(db).SetStatusAsync(jan, AccountingPeriodStatus.Open, "admin", "ajuste");
        Assert.Equal(AccountingPeriodStatus.Open, reopened.Status);
        Assert.Equal("ajuste", reopened.ReopenReason);
    }

    [Fact]
    public async Task Reopen_rejects_when_next_closed()
    {
        var (db, p) = await YearAsync();
        await Service(db).SetStatusAsync(p.Single(x => x.Month == 1).Id, AccountingPeriodStatus.Closed, "admin", null);
        await Service(db).SetStatusAsync(p.Single(x => x.Month == 2).Id, AccountingPeriodStatus.Closed, "admin", null);
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).SetStatusAsync(p.Single(x => x.Month == 1).Id, AccountingPeriodStatus.Open, "admin", "x"));
        Assert.Equal("Reabra antes o período 02/2026.", ex.Message);
    }

    [Fact]
    public async Task Closed_period_cannot_be_blocked_to_bypass_reopen_rules()
    {
        var (db, p) = await YearAsync();
        var jan = p.Single(x => x.Month == 1).Id;
        await Service(db).SetStatusAsync(jan, AccountingPeriodStatus.Closed, "admin", null);
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db, admin: false).SetStatusAsync(jan, AccountingPeriodStatus.Blocked, "joao", null));
        Assert.Equal("Só é possível bloquear um período aberto.", ex.Message);
    }

    [Fact]
    public async Task Unblock_needs_no_reason_and_no_admin()
    {
        var (db, p) = await YearAsync();
        var mar = p.Single(x => x.Month == 3).Id;
        await Service(db, admin: false).SetStatusAsync(mar, AccountingPeriodStatus.Blocked, "joao", null);
        var open = await Service(db, admin: false).SetStatusAsync(mar, AccountingPeriodStatus.Open, "joao", null);
        Assert.Equal(AccountingPeriodStatus.Open, open.Status);
    }
}
