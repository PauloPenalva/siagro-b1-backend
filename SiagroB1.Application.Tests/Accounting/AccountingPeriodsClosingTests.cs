using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services.Accounting;
using SiagroB1.Application.Services.Accounting.Offline;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.Accounting.ResultClosingServiceTests;

namespace SiagroB1.Application.Tests.Accounting;

/// <summary>Fechar período (CTBA012): exige apuração conforme a periodicidade e avisa off-line pendente.</summary>
public class AccountingPeriodsClosingTests
{
    private sealed class AdminPermissions : IUserPermissions
    {
        public Task<bool> HasAsync(string username, string permissionCode) => Task.FromResult(false);
        public Task<List<string>> GetAsync(string username) => Task.FromResult(new List<string>());
        public Task<bool> HasRoleAsync(string username, string roleCode) => Task.FromResult(true);
    }

    private static AccountingPeriodsService Periods(IUnitOfWork db, params IOfflinePostingSource[] sources) =>
        new(db, new AdminPermissions(), Service(db), new AccountingSettingsService(db), sources);

    private static async Task<(IUnitOfWork Db, List<AccountingPeriod> Periods)> YearAsync(
        AccountingClosingFrequency frequency = AccountingClosingFrequency.Monthly)
    {
        var db = await SeedAsync();
        (await db.Context.AccountingSettings.SingleAsync()).ClosingFrequency = frequency;
        await db.SaveChangesAsync();
        var periods = await Periods(db).GenerateYearAsync(2026, "admin");
        return (db, periods);
    }

    private static async Task CloseUntilAsync(IUnitOfWork db, List<AccountingPeriod> periods, int month)
    {
        foreach (var p in periods.Where(p => p.Month <= month).OrderBy(p => p.Month))
            await Periods(db).SetStatusAsync(p.Id, AccountingPeriodStatus.Closed, "admin", null);
    }

    [Fact]
    public async Task Close_refused_with_unclosed_result_monthly()
    {
        var (db, periods) = await YearAsync();
        await PostAsync(db, "1.1.01", "3.1.01", 100m, new DateTime(2026, 1, 10), branch: "02");

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Periods(db).SetStatusAsync(periods.Single(p => p.Month == 1).Id, AccountingPeriodStatus.Closed, "admin", null));
        Assert.Equal("Há resultado não apurado em 01/2026 (filial 02). Faça a apuração antes de fechar.", e.Message);
    }

    [Fact]
    public async Task Close_allowed_after_result_closing()
    {
        var (db, periods) = await YearAsync();
        await PostAsync(db, "1.1.01", "3.1.01", 100m, new DateTime(2026, 1, 10));
        await Service(db).CloseAsync(new DateTime(2026, 1, 31), null, null, "admin", CancellationToken.None);

        var jan = await Periods(db).SetStatusAsync(periods.Single(p => p.Month == 1).Id, AccountingPeriodStatus.Closed, "admin", null);
        Assert.Equal(AccountingPeriodStatus.Closed, jan.Status);
    }

    [Fact] // [RF 5]
    public async Task Yearly_allows_november_refuses_december()
    {
        var (db, periods) = await YearAsync(AccountingClosingFrequency.Yearly);
        await PostAsync(db, "1.1.01", "3.1.01", 100m, new DateTime(2026, 3, 10));

        await CloseUntilAsync(db, periods, 11);
        Assert.Equal(AccountingPeriodStatus.Closed,
            (await db.Context.AccountingPeriods.AsNoTracking().SingleAsync(p => p.Month == 11)).Status);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Periods(db).SetStatusAsync(periods.Single(p => p.Month == 12).Id, AccountingPeriodStatus.Closed, "admin", null));
        Assert.StartsWith("Há resultado não apurado em 12/2026", e.Message);
    }

    [Fact]
    public async Task Block_has_no_result_requirement()
    {
        var (db, periods) = await YearAsync();
        await PostAsync(db, "1.1.01", "3.1.01", 100m, new DateTime(2026, 1, 10));

        var jan = await Periods(db).SetStatusAsync(periods.Single(p => p.Month == 1).Id, AccountingPeriodStatus.Blocked, "admin", null);
        Assert.Equal(AccountingPeriodStatus.Blocked, jan.Status);
    }

    [Fact]
    public async Task Warnings_count_pending_offline_documents()
    {
        var (db, periods) = await YearAsync();
        db.Context.StandardPostings.Add(Postings.AccountingPostingServiceTests.Lp("650",
            Postings.AccountingPostingServiceTests.Seq("001", "Fixed", "1.1.01", "Fixed", "3.1.01", "Line.FiscalStockCost")));
        db.Context.PurchaseInvoices.AddRange(InvoiceReportSeed.Purchase("NF1", new DateTime(2026, 1, 10)),
            InvoiceReportSeed.Purchase("NF2", new DateTime(2026, 2, 10)));
        await db.SaveChangesAsync();

        var warnings = await Periods(db, new PurchaseInvoiceOfflineSource(db))
            .CloseWarningsAsync(periods.Single(p => p.Month == 1).Id, CancellationToken.None);

        Assert.Equal(["A contabilização off-line encontraria 1 documento(s) não contabilizado(s) em 01/2026."], warnings);
    }

    [Fact]
    public async Task Warnings_empty_when_nothing_pending()
    {
        var (db, periods) = await YearAsync();
        Assert.Empty(await Periods(db, new PurchaseInvoiceOfflineSource(db))
            .CloseWarningsAsync(periods.Single(p => p.Month == 1).Id, CancellationToken.None));
    }
}
