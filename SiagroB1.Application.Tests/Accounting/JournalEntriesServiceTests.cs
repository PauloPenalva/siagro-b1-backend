using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Accounting;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Web.Controllers;
using static SiagroB1.Application.Tests.Accounting.AccountingTestData;
using static SiagroB1.Application.Tests.Accounting.JournalEntryPostingServiceTests;

namespace SiagroB1.Application.Tests.Accounting;

public class JournalEntriesServiceTests
{
    private static JournalEntriesService Svc(IUnitOfWork db) => new(db, Service(db));

    private static JournalEntryDraft Manual()
    {
        var d = Draft(Double("1.1.01", "3.1.01", 100m, "Venda à vista"));
        d.Batch = string.Empty;
        return d;
    }

    [Fact]
    public async Task Manual_entry_saved_with_default_batch_and_manual_origin()
    {
        var db = await NewDbAsync();
        var draft = Manual();
        draft.Origin = JournalEntryOrigin.StandardPosting;
        draft.StandardPostingCode = "650";

        var entry = await Svc(db).CreateManualAsync(draft, "admin");

        var saved = await db.Context.JournalEntries.Include(e => e.Lines).SingleAsync();
        Assert.Equal(entry.DocEntry, saved.DocEntry);
        Assert.Equal("008890", saved.Batch);
        Assert.Equal(JournalEntryOrigin.Manual, saved.Origin);
        Assert.Null(saved.StandardPostingCode);
        Assert.Null(saved.SourceDocumentKey);
    }

    [Fact]
    public async Task Reverse_swaps_sides_and_links()
    {
        var db = await NewDbAsync();
        var draft = Draft(
            new JournalEntryLineDraft { EntryType = JournalEntryLineType.Debit, DebitAccountCode = "4.1.01", Amount = 30m, History = "D", DebitCostCenterCode = "CC01" },
            new JournalEntryLineDraft { EntryType = JournalEntryLineType.Credit, CreditAccountCode = "3.1.01", Amount = 30m, History = "C" });
        var original = await Svc(db).CreateManualAsync(draft, "admin");

        var reversal = await Svc(db).ReverseAsync(original.DocEntry, Today.AddDays(1), "joao");

        Assert.Equal(JournalEntryOrigin.Reversal, reversal.Origin);
        Assert.Equal(original.DocEntry, reversal.ReversalOfDocEntry);
        Assert.Equal(original.Batch, reversal.Batch);
        Assert.Equal(Today.AddDays(1), reversal.PostingDate);
        var credit = reversal.Lines.Single(l => l.EntryType == JournalEntryLineType.Credit);
        Assert.Equal("4.1.01", credit.CreditAccountCode);
        Assert.Equal("CC01", credit.CreditCostCenterCode);
        Assert.Equal("4.1.01", reversal.Lines.Single(l => l.EntryType == JournalEntryLineType.Credit).CreditAccountCode);
        Assert.Equal("3.1.01", reversal.Lines.Single(l => l.EntryType == JournalEntryLineType.Debit).DebitAccountCode);
        Assert.StartsWith($"Estorno doc {original.Batch}/{original.DocumentNumber}", reversal.Lines[0].History);

        var reloaded = await db.Context.JournalEntries.AsNoTracking().SingleAsync(e => e.DocEntry == original.DocEntry);
        Assert.Equal(reversal.DocEntry, reloaded.ReversedByDocEntry);
    }

    [Fact]
    public async Task Reverse_keeps_source_so_event_can_be_posted_again()
    {
        var db = await NewDbAsync();
        var draft = Draft(Double("1.1.01", "3.1.01", 10m));
        draft.Origin = JournalEntryOrigin.StandardPosting;
        draft.StandardPostingCode = "650";
        draft.SourceDocumentType = "PurchaseInvoice";
        draft.SourceDocumentKey = "K1";
        draft.SourceEvent = "Confirm";
        var original = await Service(db).AddAsync(draft, "admin");
        await db.SaveChangesAsync();

        var reversal = await Svc(db).ReverseAsync(original.DocEntry, Today, "admin");
        Assert.Equal("K1", reversal.SourceDocumentKey);
        Assert.Equal("650", reversal.StandardPostingCode);
    }

    [Fact]
    public async Task Cannot_reverse_twice()
    {
        var db = await NewDbAsync();
        var original = await Svc(db).CreateManualAsync(Manual(), "admin");
        await Svc(db).ReverseAsync(original.DocEntry, Today, "admin");
        var ex = await Assert.ThrowsAsync<DefaultException>(() => Svc(db).ReverseAsync(original.DocEntry, Today, "admin"));
        Assert.Equal("Este lançamento já foi estornado.", ex.Message);
    }

    [Fact]
    public async Task Cannot_reverse_a_reversal()
    {
        var db = await NewDbAsync();
        var original = await Svc(db).CreateManualAsync(Manual(), "admin");
        var reversal = await Svc(db).ReverseAsync(original.DocEntry, Today, "admin");
        var ex = await Assert.ThrowsAsync<DefaultException>(() => Svc(db).ReverseAsync(reversal.DocEntry, Today, "admin"));
        Assert.Equal("Um estorno não pode ser estornado.", ex.Message);
    }

    [Fact]
    public async Task Reverse_rejects_closed_period_on_reversal_date()
    {
        var db = await NewDbAsync();
        var original = await Svc(db).CreateManualAsync(Manual(), "admin");
        db.Context.AccountingPeriods.Add(new AccountingPeriod
        {
            Year = 2026, Month = 11, StartDate = new DateTime(2026, 11, 1), EndDate = new DateTime(2026, 11, 30),
            Status = AccountingPeriodStatus.Closed,
        });
        await db.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Svc(db).ReverseAsync(original.DocEntry, new DateTime(2026, 11, 5), "admin"));
        Assert.Equal("Período contábil 11/2026 fechado.", ex.Message);
        Assert.Null((await db.Context.JournalEntries.AsNoTracking().SingleAsync(e => e.DocEntry == original.DocEntry)).ReversedByDocEntry);
    }

    [Fact]
    public void Controller_has_no_patch_or_delete()
    {
        var methods = typeof(JournalEntriesController).GetMethods().Select(m => m.Name).ToList();
        Assert.DoesNotContain(methods, m => m.StartsWith("Patch") || m.StartsWith("Put") || m.StartsWith("Delete"));
    }
}
