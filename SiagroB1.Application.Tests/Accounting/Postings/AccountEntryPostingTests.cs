using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;
using static SiagroB1.Application.Tests.Accounting.Postings.AccountingPostingServiceTests;

namespace SiagroB1.Application.Tests.Accounting.Postings;

/// <summary>Lançamento na conta (562/563, exclusão 564/565) e transferência (560/561), com cancelamento lógico.</summary>
public class AccountEntryPostingTests
{
    private static async Task<IUnitOfWork> SeedAsync(string? accountBranch = "02")
    {
        var db = await AccountingPostingServiceTests.SeedAsync();
        db.Context.Branchs.AddRange(new Branch { Code = "02", BranchName = "Filial 2" }, new Branch { Code = "05", BranchName = "Filial 5" });
        db.Context.LedgerAccounts.AddRange(
            new LedgerAccount { Code = "1.1.01", Name = "Banco", Type = LedgerAccountType.Asset },
            new LedgerAccount { Code = "1.1.05", Name = "Caixa", Type = LedgerAccountType.Asset });
        db.Context.FinancialAccounts.AddRange(
            new FinancialAccount { Code = "BCO1", Name = "Banco", Type = FinancialAccountType.Bank, Currency = CurrencyType.Brl, LedgerAccountCode = "1.1.01", BranchCode = accountBranch },
            new FinancialAccount { Code = "CX1", Name = "Caixa", Type = FinancialAccountType.Cash, Currency = CurrencyType.Brl, LedgerAccountCode = "1.1.05", BranchCode = "02" });
        db.Context.StandardPostings.AddRange(
            Lp("562", Seq("001", "Fixed", "4.1.01", "FinancialAccount.LedgerAccount", null, "Entry.Amount", "{Description}")),
            Lp("563", Seq("001", "FinancialAccount.LedgerAccount", null, "Fixed", "2.1.01", "Entry.Amount")),
            Lp("565", Seq("001", "Fixed", "2.1.01", "FinancialAccount.LedgerAccount", null, "Entry.Amount")),
            Lp("560", Seq("001", "TargetFinancialAccount.LedgerAccount", null, "FinancialAccount.LedgerAccount", null, "Transfer.Amount")));
        await db.SaveChangesAsync();
        return db;
    }

    private static Task<FinancialAccountEntry> Entry(IUnitOfWork db, decimal amount, string account = "BCO1") =>
        new FinancialAccountEntriesCreateService(db, accounting: Service(db)).ExecuteAsync(new FinancialAccountEntry
        {
            FinancialAccountCode = account, EntryDate = Day, Amount = amount, Description = "Tarifa",
        }, "admin");

    private static Task<List<JournalEntry>> Entries(IUnitOfWork db) =>
        db.Context.JournalEntries.AsNoTracking().Include(e => e.Lines).OrderBy(e => e.DocEntry).ToListAsync();

    [Fact]
    public async Task Entry_posts_563_or_562_by_sign()
    {
        var db = await SeedAsync();
        await Entry(db, -30m);
        await Entry(db, 80m);

        var entries = await Entries(db);
        Assert.Equal(["562", "563"], entries.Select(e => e.StandardPostingCode));
        Assert.Equal(30m, entries[0].Lines.Single().Amount);
        Assert.Equal(("4.1.01", "1.1.01"), (entries[0].Lines.Single().DebitAccountCode, entries[0].Lines.Single().CreditAccountCode));
        Assert.Equal("Tarifa", entries[0].Lines.Single().History);
        Assert.Equal("02", entries[0].BranchCode);
    }

    [Fact]
    public async Task Branch_falls_back_to_lowest_branch()
    {
        var db = await SeedAsync(accountBranch: null);
        await Entry(db, 10m);
        Assert.Equal("02", (await Entries(db)).Single().BranchCode);
    }

    [Fact]
    public async Task Delete_entry_marks_canceled_and_keeps_row()
    {
        var db = await SeedAsync();
        var created = await Entry(db, 80m);

        await new FinancialAccountEntriesDeleteService(db, Service(db)).ExecuteAsync(created.Key, "joao");

        Assert.Empty(await db.Context.FinancialAccountEntries.ToListAsync());
        var row = await db.Context.FinancialAccountEntries.IgnoreQueryFilters().SingleAsync();
        Assert.NotNull(row.CanceledAt);
        Assert.Equal("joao", row.CanceledBy);
    }

    [Fact]
    public async Task Canceled_entry_leaves_balance_and_statement()
    {
        var db = await SeedAsync();
        await Entry(db, 100m);
        var gone = await Entry(db, 40m);
        await new FinancialAccountEntriesDeleteService(db, Service(db)).ExecuteAsync(gone.Key, "joao");

        var statement = await FinancialAccountMovementsQuery.StatementAsync(
            db.Context, "BCO1", Day.AddDays(-1), Day.AddDays(1), false, CancellationToken.None);
        Assert.Equal(100m, statement.ClosingBalance);
    }

    [Fact]
    public async Task Cancel_posted_entry_posts_565()
    {
        var db = await SeedAsync();
        var created = await Entry(db, 80m);
        await new FinancialAccountEntriesDeleteService(db, Service(db)).ExecuteAsync(created.Key, "joao");

        var entries = await Entries(db);
        Assert.Equal(["563", "565"], entries.Select(e => e.StandardPostingCode));
        Assert.Equal(entries[0].DocEntry, entries[1].ReversalOfDocEntry);
    }

    [Fact]
    public async Task Transfer_posts_560_not_legs()
    {
        var db = await SeedAsync();
        await new FinancialAccountTransfersCreateService(db, Service(db)).ExecuteAsync(new FinancialAccountTransfer
        {
            FromAccountCode = "BCO1", ToAccountCode = "CX1", TransferDate = Day, Amount = 70m,
        }, "admin");

        var entry = (await Entries(db)).Single();
        Assert.Equal("560", entry.StandardPostingCode);
        Assert.Equal(("1.1.05", "1.1.01"), (entry.Lines.Single().DebitAccountCode, entry.Lines.Single().CreditAccountCode));
    }

    [Fact]
    public async Task Delete_transfer_cancels_legs_and_auto_reverses_560()
    {
        var db = await SeedAsync();
        var transfer = await new FinancialAccountTransfersCreateService(db, Service(db)).ExecuteAsync(new FinancialAccountTransfer
        {
            FromAccountCode = "BCO1", ToAccountCode = "CX1", TransferDate = Day, Amount = 70m,
        }, "admin");

        await new FinancialAccountTransfersDeleteService(db, Service(db)).ExecuteAsync(transfer.Key, "joao");

        Assert.Empty(await db.Context.FinancialAccountEntries.ToListAsync());
        Assert.Empty(await db.Context.FinancialAccountTransfers.ToListAsync());
        Assert.Equal(2, await db.Context.FinancialAccountEntries.IgnoreQueryFilters().CountAsync(e => e.CanceledAt != null));
        Assert.Equal(["560", "560"], (await Entries(db)).Select(e => e.StandardPostingCode)); // 561 não configurado: espelho
    }
}
