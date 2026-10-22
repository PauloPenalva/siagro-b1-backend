using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Accounting;

public interface IJournalEntriesService
{
    IQueryable<JournalEntry> QueryAll();

    Task<JournalEntry> CreateManualAsync(JournalEntryDraft draft, string user);

    Task<JournalEntry> ReverseAsync(int docEntry, DateTime date, string user);

    /// <summary>Estorno sem gravar (a contabilização automática grava junto com o documento de origem).</summary>
    Task<JournalEntry> AddReversalAsync(JournalEntry original, DateTime date, string user);
}

/// <summary>Lançamento manual e estorno (CTBA102). Lançamento gravado não se altera nem se apaga.</summary>
public class JournalEntriesService(IUnitOfWork db, IJournalEntryPostingService posting) : IJournalEntriesService
{
    public const string ManualBatch = "008890";

    public IQueryable<JournalEntry> QueryAll() => db.Context.JournalEntries.AsNoTracking();

    public async Task<JournalEntry> CreateManualAsync(JournalEntryDraft draft, string user)
    {
        draft.Origin = JournalEntryOrigin.Manual;
        draft.StandardPostingCode = null;
        draft.SourceDocumentType = null;
        draft.SourceDocumentKey = null;
        draft.SourceEvent = null;
        draft.ReversalOfDocEntry = null;
        if (string.IsNullOrWhiteSpace(draft.Batch)) draft.Batch = ManualBatch;

        var entry = await posting.AddAsync(draft, user);
        await db.SaveChangesAsync();
        return entry;
    }

    public async Task<JournalEntry> ReverseAsync(int docEntry, DateTime date, string user)
    {
        var original = await db.Context.JournalEntries.Include(e => e.Lines)
                           .FirstOrDefaultAsync(e => e.DocEntry == docEntry)
                       ?? throw new DefaultException("Lançamento contábil não encontrado.");
        var reversal = await AddReversalAsync(original, date, user);
        await db.SaveChangesAsync();
        return reversal;
    }

    public async Task<JournalEntry> AddReversalAsync(JournalEntry original, DateTime date, string user)
    {
        if (original.ReversalOfDocEntry is not null) throw new DefaultException("Um estorno não pode ser estornado.");
        if (original.ReversedByDocEntry is not null || original.ReversedBy is not null)
            throw new DefaultException("Este lançamento já foi estornado.");

        var lines = original.Lines.Count > 0
            ? original.Lines
            : await db.Context.JournalEntryLines.AsNoTracking().Where(l => l.DocEntry == original.DocEntry).ToListAsync();
        var history = $"Estorno doc {original.Batch}/{original.DocumentNumber}";

        var draft = new JournalEntryDraft
        {
            PostingDate = date,
            BranchCode = original.BranchCode,
            Batch = original.Batch,
            SubBatch = original.SubBatch,
            Origin = JournalEntryOrigin.Reversal,
            StandardPostingCode = original.StandardPostingCode,
            SourceDocumentType = original.SourceDocumentType,
            SourceDocumentKey = original.SourceDocumentKey,
            SourceEvent = original.SourceEvent,
            ReversalOfDocEntry = original.DocEntry,
            Lines = lines.Where(l => l.EntryType != JournalEntryLineType.HistoryContinuation)
                .OrderBy(l => l.LineNumber)
                .Select(l => new JournalEntryLineDraft
                {
                    EntryType = l.EntryType switch
                    {
                        JournalEntryLineType.Debit => JournalEntryLineType.Credit,
                        JournalEntryLineType.Credit => JournalEntryLineType.Debit,
                        _ => l.EntryType,
                    },
                    DebitAccountCode = l.CreditAccountCode,
                    CreditAccountCode = l.DebitAccountCode,
                    DebitCostCenterCode = l.CreditCostCenterCode,
                    CreditCostCenterCode = l.DebitCostCenterCode,
                    DebitAccountingItemCode = l.CreditAccountingItemCode,
                    CreditAccountingItemCode = l.DebitAccountingItemCode,
                    Amount = l.Amount,
                    History = history,
                    StandardPostingSequence = l.StandardPostingSequence,
                    BusinessPartnerCode = l.BusinessPartnerCode,
                }).ToList(),
        };

        var reversal = await posting.AddAsync(draft, user);
        reversal.ReversalOf = original;
        original.ReversedBy = reversal;
        return reversal;
    }
}
