using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Accounting;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Accounting;

namespace SiagroB1.Application.Tests.Accounting;

/// <summary>Apuração de resultado (CTBA211): zera as contas de resultado contra a conta de apuração, por filial.</summary>
public class ResultClosingServiceTests
{
    private static readonly DateTime Oct31 = new(2026, 10, 31);

    public static async Task<IUnitOfWork> SeedAsync(string? defaultClosing = "2.4.01")
    {
        var db = TestDb.CreateUnitOfWork(Guid.NewGuid().ToString());
        await AccountingTestData.SeedAsync(db); // CC01, CC02
        db.Context.LedgerAccounts.AddRange(
            new LedgerAccount { Code = "1.1.01", Name = "Caixa", Type = LedgerAccountType.Asset, Nature = LedgerAccountNature.Debit },
            new LedgerAccount { Code = "2.4.01", Name = "Resultado do exercício", Type = LedgerAccountType.Equity, Nature = LedgerAccountNature.Credit, AcceptsCostCenter = false, AcceptsAccountingItem = false },
            new LedgerAccount { Code = "2.4.02", Name = "Apuração específica", Type = LedgerAccountType.Equity, Nature = LedgerAccountNature.Credit, AcceptsCostCenter = false, AcceptsAccountingItem = false },
            new LedgerAccount { Code = "3.1.01", Name = "Vendas", Type = LedgerAccountType.Revenue, Nature = LedgerAccountNature.Credit },
            new LedgerAccount { Code = "4.1.01", Name = "Despesas", Type = LedgerAccountType.Expense, Nature = LedgerAccountNature.Debit });
        db.Context.AccountingSettings.Add(new AccountingSettings
        {
            CutoverDate = new DateTime(2026, 1, 1), DefaultResultClosingAccountCode = defaultClosing, UpdatedBy = "t",
        });
        await db.SaveChangesAsync();
        return db;
    }

    /// <summary>Lançamento manual: débito em <paramref name="debit"/>, crédito em <paramref name="credit"/>.</summary>
    public static async Task PostAsync(IUnitOfWork db, string debit, string credit, decimal amount, DateTime date,
        string branch = "01", string? debitCc = null, string? creditCc = null)
    {
        var posting = new JournalEntryPostingService(db, new AccountingPeriodGuard(db));
        await posting.AddAsync(new JournalEntryDraft
        {
            PostingDate = date, BranchCode = branch, Batch = "008890", Origin = JournalEntryOrigin.Manual,
            Lines =
            [
                new JournalEntryLineDraft
                {
                    EntryType = JournalEntryLineType.Double, DebitAccountCode = debit, CreditAccountCode = credit, Amount = amount,
                    History = "Teste", DebitCostCenterCode = debitCc, CreditCostCenterCode = creditCc,
                },
            ],
        }, "t");
        await db.SaveChangesAsync();
    }

    public static ResultClosingService Service(IUnitOfWork db)
    {
        var guard = new AccountingPeriodGuard(db);
        var posting = new JournalEntryPostingService(db, guard);
        return new ResultClosingService(db, posting, new JournalEntriesService(db, posting), new AccountingSettingsService(db));
    }

    private static async Task<Dictionary<string, decimal>> BalancesAsync(IUnitOfWork db, string branch = "01")
    {
        var lines = await (from l in db.Context.JournalEntryLines
                           join e in db.Context.JournalEntries on l.DocEntry equals e.DocEntry
                           where e.BranchCode == branch
                           select l).ToListAsync();
        var result = new Dictionary<string, decimal>();
        foreach (var l in lines)
        {
            if (l.DebitAccountCode is not null) result[l.DebitAccountCode] = result.GetValueOrDefault(l.DebitAccountCode) + l.Amount;
            if (l.CreditAccountCode is not null) result[l.CreditAccountCode] = result.GetValueOrDefault(l.CreditAccountCode) - l.Amount;
        }
        return result;
    }

    [Fact]
    public async Task Close_zeroes_each_result_account_against_closing_account()
    {
        var db = await SeedAsync();
        await PostAsync(db, "1.1.01", "3.1.01", 1000m, new DateTime(2026, 10, 5));
        await PostAsync(db, "4.1.01", "1.1.01", 300m, new DateTime(2026, 10, 6));

        var entries = await Service(db).CloseAsync(Oct31, null, null, "admin", CancellationToken.None);

        var entry = Assert.Single(entries);
        Assert.Equal(JournalEntryOrigin.ResultClosing, entry.Origin);
        Assert.Equal("008860", entry.Batch);
        Assert.Equal(Oct31, entry.PostingDate);
        var balances = await BalancesAsync(db);
        Assert.Equal(0m, balances["3.1.01"]);
        Assert.Equal(0m, balances["4.1.01"]);
        Assert.Equal(-700m, balances["2.4.01"]); // lucro de 700 a crédito
        Assert.Contains(entry.Lines, l => l.History == "Apuração de resultado 10/2026");
    }

    [Fact] // [RF 1]
    public async Task Close_keeps_cost_center_on_result_lines()
    {
        var db = await SeedAsync();
        await PostAsync(db, "4.1.01", "1.1.01", 100m, new DateTime(2026, 10, 5), debitCc: "CC01");
        await PostAsync(db, "4.1.01", "1.1.01", 50m, new DateTime(2026, 10, 5), debitCc: "CC02");

        var entry = Assert.Single(await Service(db).CloseAsync(Oct31, null, null, "admin", CancellationToken.None));

        var resultLines = entry.Lines.Where(l => l.CreditAccountCode == "4.1.01").ToList();
        Assert.Equal(["CC01", "CC02"], resultLines.Select(l => l.CreditCostCenterCode).Order());
        var counter = Assert.Single(entry.Lines, l => l.DebitAccountCode == "2.4.01");
        Assert.Equal(150m, counter.Amount);
        Assert.Null(counter.DebitCostCenterCode);
        Assert.Equal(entry.Lines.Where(l => l.DebitAccountCode != null).Sum(l => l.Amount),
            entry.Lines.Where(l => l.CreditAccountCode != null).Sum(l => l.Amount));
    }

    [Fact]
    public async Task Close_uses_account_specific_closing_account()
    {
        var db = await SeedAsync();
        (await db.Context.LedgerAccounts.SingleAsync(a => a.Code == "3.1.01")).ResultClosingAccountCode = "2.4.02";
        await db.SaveChangesAsync();
        await PostAsync(db, "1.1.01", "3.1.01", 200m, new DateTime(2026, 10, 5));

        await Service(db).CloseAsync(Oct31, null, null, "admin", CancellationToken.None);

        var balances = await BalancesAsync(db);
        Assert.Equal(-200m, balances["2.4.02"]);
        Assert.False(balances.ContainsKey("2.4.01"));
    }

    [Fact]
    public async Task Close_without_closing_account_is_refused()
    {
        var db = await SeedAsync(defaultClosing: null);
        await PostAsync(db, "1.1.01", "3.1.01", 200m, new DateTime(2026, 10, 5));

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).CloseAsync(Oct31, null, null, "admin", CancellationToken.None));
        Assert.Equal("Conta 3.1.01: informe a conta de apuração (na conta ou nas Configurações Contábeis).", e.Message);
    }

    [Fact]
    public async Task Close_creates_one_entry_per_branch()
    {
        var db = await SeedAsync();
        await PostAsync(db, "1.1.01", "3.1.01", 200m, new DateTime(2026, 10, 5), branch: "01");
        await PostAsync(db, "1.1.01", "3.1.01", 80m, new DateTime(2026, 10, 5), branch: "02");

        var entries = await Service(db).CloseAsync(Oct31, null, null, "admin", CancellationToken.None);

        Assert.Equal(["01", "02"], entries.Select(e => e.BranchCode).Order());
        Assert.Empty(await Service(db).UnclosedAsync(Oct31, null, CancellationToken.None));
    }

    [Fact]
    public async Task Close_only_given_branch()
    {
        var db = await SeedAsync();
        await PostAsync(db, "1.1.01", "3.1.01", 200m, new DateTime(2026, 10, 5), branch: "01");
        await PostAsync(db, "1.1.01", "3.1.01", 80m, new DateTime(2026, 10, 5), branch: "02");

        var entry = Assert.Single(await Service(db).CloseAsync(Oct31, "02", null, "admin", CancellationToken.None));

        Assert.Equal("02", entry.BranchCode);
        Assert.Equal("01", Assert.Single(await Service(db).UnclosedAsync(Oct31, null, CancellationToken.None)).BranchCode);
    }

    [Fact]
    public async Task Close_again_same_date_returns_nothing()
    {
        var db = await SeedAsync();
        await PostAsync(db, "1.1.01", "3.1.01", 200m, new DateTime(2026, 10, 5));
        await Service(db).CloseAsync(Oct31, null, null, "admin", CancellationToken.None);

        Assert.Empty(await Service(db).CloseAsync(Oct31, null, null, "admin", CancellationToken.None));
        Assert.Single(await db.Context.JournalEntries.Where(e => e.Origin == JournalEntryOrigin.ResultClosing).ToListAsync());
    }

    [Fact] // [RF 2]
    public async Task Close_in_closed_period_is_refused()
    {
        var db = await SeedAsync();
        await PostAsync(db, "1.1.01", "3.1.01", 200m, new DateTime(2026, 10, 5));
        db.Context.AccountingPeriods.Add(new AccountingPeriod
        {
            Year = 2026, Month = 10, StartDate = new DateTime(2026, 10, 1), EndDate = Oct31, Status = AccountingPeriodStatus.Closed,
        });
        await db.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).CloseAsync(Oct31, null, null, "admin", CancellationToken.None));
        Assert.Contains("fechado", e.Message);
        Assert.Equal(0, await db.Context.JournalEntries.CountAsync(x => x.Origin == JournalEntryOrigin.ResultClosing));
    }

    [Fact]
    public async Task Reverse_latest_reverses_most_recent_per_branch()
    {
        var db = await SeedAsync();
        await PostAsync(db, "1.1.01", "3.1.01", 200m, new DateTime(2026, 9, 5));
        await Service(db).CloseAsync(new DateTime(2026, 9, 30), null, null, "admin", CancellationToken.None);
        await PostAsync(db, "1.1.01", "3.1.01", 100m, new DateTime(2026, 10, 5));
        var october = Assert.Single(await Service(db).CloseAsync(Oct31, null, null, "admin", CancellationToken.None));

        var reversal = Assert.Single(await Service(db).ReverseLatestAsync(null, "admin", CancellationToken.None));

        Assert.Equal(october.DocEntry, reversal.ReversalOfDocEntry);
        Assert.Equal(Oct31, reversal.PostingDate);
        Assert.Equal(-100m, Assert.Single(await Service(db).UnclosedAsync(Oct31, null, CancellationToken.None)).Balance);
    }

    [Fact]
    public async Task Reverse_without_closing_is_refused()
    {
        var db = await SeedAsync();
        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).ReverseLatestAsync(null, "admin", CancellationToken.None));
        Assert.Equal("Não há apuração para estornar.", e.Message);
    }

    [Fact]
    public async Task Close_ignores_balance_sheet_accounts()
    {
        var db = await SeedAsync();
        await PostAsync(db, "1.1.01", "2.4.02", 500m, new DateTime(2026, 10, 5));

        Assert.Empty(await Service(db).UnclosedAsync(Oct31, null, CancellationToken.None));
        Assert.Empty(await Service(db).CloseAsync(Oct31, null, null, "admin", CancellationToken.None));
    }

    [Fact]
    public async Task Close_respects_date()
    {
        var db = await SeedAsync();
        await PostAsync(db, "1.1.01", "3.1.01", 200m, new DateTime(2026, 10, 5));
        await PostAsync(db, "1.1.01", "3.1.01", 50m, new DateTime(2026, 11, 5));

        await Service(db).CloseAsync(Oct31, null, null, "admin", CancellationToken.None);

        Assert.Equal(-50m, Assert.Single(await Service(db).UnclosedAsync(new DateTime(2026, 11, 30), null, CancellationToken.None)).Balance);
    }
}
