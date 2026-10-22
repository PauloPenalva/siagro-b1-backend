using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Accounting;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Accounting;
using static SiagroB1.Application.Tests.Accounting.AccountingTestData;

namespace SiagroB1.Application.Tests.Accounting;

public class JournalEntryPostingServiceTests
{
    public static async Task<IUnitOfWork> NewDbAsync(string? name = null)
    {
        var db = TestDb.CreateUnitOfWork(name ?? Guid.NewGuid().ToString());
        await SeedAsync(db);
        db.Context.LedgerAccounts.AddRange(
            new LedgerAccount { Code = "1", Name = "Ativo", Type = LedgerAccountType.Asset, AllowsPosting = false },
            new LedgerAccount { Code = "1.1.01", Name = "Caixa", Type = LedgerAccountType.Asset, ParentCode = "1", Nature = LedgerAccountNature.Debit, AcceptsCostCenter = false, AcceptsAccountingItem = false },
            new LedgerAccount { Code = "3.1.01", Name = "Receita", Type = LedgerAccountType.Revenue, Nature = LedgerAccountNature.Credit },
            new LedgerAccount { Code = "4.1.01", Name = "Despesa", Type = LedgerAccountType.Expense, Nature = LedgerAccountNature.Debit, RequiresCostCenter = true },
            new LedgerAccount { Code = "9.9.99", Name = "Bloqueada", Type = LedgerAccountType.Expense, BlockedFrom = Today.AddDays(-5) });
        await db.SaveChangesAsync();
        return db;
    }

    public static JournalEntryPostingService Service(IUnitOfWork db) => new(db, new AccountingPeriodGuard(db));

    public static JournalEntryDraft Draft(params JournalEntryLineDraft[] lines) => new()
    {
        PostingDate = Today, BranchCode = "01", Batch = "008890", Origin = JournalEntryOrigin.Manual, Lines = lines.ToList(),
    };

    public static JournalEntryLineDraft Double(string debit, string credit, decimal amount, string history = "Teste") => new()
    {
        EntryType = JournalEntryLineType.Double, DebitAccountCode = debit, CreditAccountCode = credit, Amount = amount, History = history,
    };

    private static JournalEntryLineDraft Debit(string account, decimal amount, string? cc = null) => new()
    {
        EntryType = JournalEntryLineType.Debit, DebitAccountCode = account, Amount = amount, History = "D", DebitCostCenterCode = cc,
    };

    private static JournalEntryLineDraft Credit(string account, decimal amount) => new()
    {
        EntryType = JournalEntryLineType.Credit, CreditAccountCode = account, Amount = amount, History = "C",
    };

    private static async Task<string> RejectAsync(IUnitOfWork db, JournalEntryDraft draft) =>
        (await Assert.ThrowsAsync<DefaultException>(() => Service(db).AddAsync(draft, "admin"))).Message;

    [Fact]
    public async Task Rejects_unbalanced_by_one_cent()
    {
        var db = await NewDbAsync();
        Assert.Equal("Lançamento desbalanceado: débito 100,00 x crédito 99,99.",
            await RejectAsync(db, Draft(Debit("1.1.01", 100m), Credit("3.1.01", 99.99m))));
    }

    [Fact]
    public async Task Rejects_amount_with_more_than_two_decimals()
    {
        // 100,004 + 100,004 = 200,008 "fecha", mas o decimal(19,2) gravaria 100,00 + 100,00 x 200,01.
        var db = await NewDbAsync();
        Assert.Equal("Linha 1: o valor deve ter no máximo 2 casas decimais.",
            await RejectAsync(db, Draft(Debit("1.1.01", 100.004m), Debit("1.1.01", 100.004m), Credit("3.1.01", 200.008m))));
    }

    [Fact]
    public async Task Rejects_empty_entry()
    {
        var db = await NewDbAsync();
        Assert.Equal("Informe as linhas do lançamento.", await RejectAsync(db, Draft()));
    }

    [Fact]
    public async Task Rejects_synthetic_account()
    {
        var db = await NewDbAsync();
        Assert.Equal("Linha 1: a conta 1 não existe, é sintética, está inativa ou bloqueada em 21/10/2026.",
            await RejectAsync(db, Draft(Double("1", "3.1.01", 10m))));
    }

    [Fact]
    public async Task Rejects_blocked_account_on_date()
    {
        var db = await NewDbAsync();
        Assert.Equal("Linha 1: a conta 9.9.99 não existe, é sintética, está inativa ou bloqueada em 21/10/2026.",
            await RejectAsync(db, Draft(Double("9.9.99", "3.1.01", 10m))));
    }

    [Fact]
    public async Task Rejects_cost_center_on_account_that_does_not_accept()
    {
        var db = await NewDbAsync();
        Assert.Equal("Linha 1: a conta 1.1.01 não aceita centro de custo.",
            await RejectAsync(db, Draft(Debit("1.1.01", 10m, "CC01"), Credit("3.1.01", 10m))));
    }

    [Fact]
    public async Task Requires_cost_center_when_account_requires()
    {
        var db = await NewDbAsync();
        Assert.Equal("Linha 1: a conta 4.1.01 exige centro de custo.",
            await RejectAsync(db, Draft(Debit("4.1.01", 10m), Credit("3.1.01", 10m))));
    }

    [Fact]
    public async Task Rejects_unusable_cost_center()
    {
        var db = await NewDbAsync();
        Assert.Equal("Linha 1: o centro de custo SYN não existe, é sintético, está bloqueado ou fora da vigência.",
            await RejectAsync(db, Draft(Debit("4.1.01", 10m, "SYN"), Credit("3.1.01", 10m))));
    }

    [Fact]
    public async Task Rejects_zero_amount_on_value_line()
    {
        var db = await NewDbAsync();
        Assert.Equal("Linha 1: informe um valor maior que zero.", await RejectAsync(db, Draft(Double("1.1.01", "3.1.01", 0m))));
    }

    [Fact]
    public async Task Rejects_debit_line_without_debit_account()
    {
        var db = await NewDbAsync();
        var line = Credit("3.1.01", 10m);
        line.EntryType = JournalEntryLineType.Debit;
        Assert.Equal("Linha 1: informe somente a conta de débito.", await RejectAsync(db, Draft(line, Credit("3.1.01", 10m))));
    }

    [Fact]
    public async Task Prefixes_standard_posting_sequence_in_messages()
    {
        var db = await NewDbAsync();
        var line = Double("1", "3.1.01", 10m);
        line.StandardPostingSequence = "002";
        var draft = Draft(line);
        draft.Origin = JournalEntryOrigin.StandardPosting;
        draft.StandardPostingCode = "650";
        Assert.StartsWith("LP 650 seq. 002: Linha 1: a conta 1", await RejectAsync(db, draft));
    }

    [Fact]
    public async Task Splits_long_history_into_type4_lines()
    {
        var db = await NewDbAsync();
        var text = new string('a', 40) + new string('b', 40) + "cc";
        var entry = await Service(db).AddAsync(Draft(Double("1.1.01", "3.1.01", 10m, text)), "admin");

        Assert.Equal(3, entry.Lines.Count);
        Assert.Equal(new string('a', 40), entry.Lines[0].History);
        Assert.Equal(JournalEntryLineType.HistoryContinuation, entry.Lines[1].EntryType);
        Assert.Equal(new string('b', 40), entry.Lines[1].History);
        Assert.Equal("cc", entry.Lines[2].History);
        Assert.Equal(0m, entry.Lines[2].Amount);
        Assert.Equal([1, 2, 3], entry.Lines.Select(l => l.LineNumber));
    }

    [Fact]
    public async Task Double_entry_line_counts_both_sides()
    {
        var db = await NewDbAsync();
        var entry = await Service(db).AddAsync(Draft(Double("1.1.01", "3.1.01", 10m), Debit("4.1.01", 5m, "CC01"), Credit("3.1.01", 5m)), "admin");
        Assert.Equal(3, entry.Lines.Count);
        Assert.Equal("admin", entry.CreatedBy);
    }

    [Fact]
    public async Task Rejects_closed_period()
    {
        var db = await NewDbAsync();
        db.Context.AccountingPeriods.Add(new AccountingPeriod
        {
            Year = 2026, Month = 10, StartDate = new DateTime(2026, 10, 1), EndDate = new DateTime(2026, 10, 31),
            Status = AccountingPeriodStatus.Closed,
        });
        await db.SaveChangesAsync();
        Assert.Equal("Período contábil 10/2026 fechado.", await RejectAsync(db, Draft(Double("1.1.01", "3.1.01", 10m))));
    }

    [Fact]
    public async Task Numbers_sequentially_per_date_batch_subbatch()
    {
        var db = await NewDbAsync();
        var first = await Service(db).AddAsync(Draft(Double("1.1.01", "3.1.01", 10m)), "admin");
        var second = await Service(db).AddAsync(Draft(Double("1.1.01", "3.1.01", 10m)), "admin");
        await db.SaveChangesAsync();
        var third = await Service(db).AddAsync(Draft(Double("1.1.01", "3.1.01", 10m)), "admin");
        var otherBatch = Draft(Double("1.1.01", "3.1.01", 10m));
        otherBatch.Batch = "008810";
        var fourth = await Service(db).AddAsync(otherBatch, "admin");

        Assert.Equal(["000001", "000002", "000003", "000001"],
            new[] { first, second, third, fourth }.Select(e => e.DocumentNumber));
    }

    [Fact]
    public async Task Does_not_save_changes()
    {
        var name = Guid.NewGuid().ToString();
        var db = await NewDbAsync(name);
        await Service(db).AddAsync(Draft(Double("1.1.01", "3.1.01", 10m)), "admin");
        Assert.Equal(0, await TestDb.CreateUnitOfWork(name).Context.JournalEntries.CountAsync());
    }

    [Fact]
    public async Task Rejects_missing_branch()
    {
        var db = await NewDbAsync();
        var draft = Draft(Double("1.1.01", "3.1.01", 10m));
        draft.BranchCode = " ";
        Assert.Equal("Informe a filial do lançamento.", await RejectAsync(db, draft));
    }
}
