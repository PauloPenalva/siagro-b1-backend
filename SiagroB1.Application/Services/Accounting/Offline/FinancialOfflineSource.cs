using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Accounting.Postings;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Accounting.Offline;

/// <summary>
/// Financeiro sem lançamento (FINA370): títulos 500/510, baixas 520/530, lançamentos na conta 562/563 e
/// transferências 560. Só o que continua valendo: título não cancelado, baixa não estornada, lançamento não cancelado
/// (o filtro global já esconde os cancelados). Filial = a mesma resolvida pelo on-line.
/// </summary>
public class FinancialOfflineSource(IUnitOfWork db) : IOfflinePostingSource
{
    public OfflinePostingModule Module => OfflinePostingModule.Financial;

    public async IAsyncEnumerable<OfflinePostingEvent> CollectAsync(DateTime from, DateTime to, string? branchCode,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var lookup = await OfflinePostingLookup.LoadAsync(db,
            [FinancialDocumentPosting.DocumentType, SettlementPosting.DocumentType, AccountEntryPosting.EntryDocumentType,
             AccountEntryPosting.TransferDocumentType], ct);
        var fallbackBranch = await PostingBranch.ResolveAsync(db.Context, null);
        var accountBranch = await db.Context.FinancialAccounts.AsNoTracking()
            .ToDictionaryAsync(a => a.Code, a => a.BranchCode, ct);
        var start = from.Date;
        var end = to.Date.AddDays(1);

        string Branch(string? first, string? second = null) =>
            !string.IsNullOrWhiteSpace(first) ? first.Trim()
            : !string.IsNullOrWhiteSpace(second) ? second.Trim()
            : fallbackBranch;
        string? AccountBranch(string? code) => code is null ? null : accountBranch.GetValueOrDefault(code);

        var events = new List<OfflinePostingEvent>();

        // Títulos
        var titles = await db.Context.FinancialDocuments.AsNoTracking()
            .Where(d => d.Status != FinancialDocumentStatus.Canceled && d.DocumentDate >= start && d.DocumentDate < end)
            .ToListAsync(ct);
        foreach (var title in titles.Where(FinancialDocumentPosting.IsPostable))
        {
            var branch = Branch(title.BranchCode);
            if (!OfflinePostingLookup.InBranch(branch, branchCode)) continue;
            var code = title.Direction == FinancialDirection.Receivable ? "500" : "510";
            var key = title.Key.ToString();
            var missing = lookup.Missing(key, FinancialDocumentPosting.CreateEvent, code);
            if (missing.Count == 0) continue;
            var titleKey = title.Key;
            events.Add(new OfflinePostingEvent(title.DocumentDate.Date, FinancialDocumentPosting.DocumentType, key,
                title.Code ?? key, branch, missing, null, async (engine, user, token) =>
                {
                    var tracked = await db.Context.FinancialDocuments.FirstAsync(d => d.Key == titleKey, token);
                    var context = await FinancialDocumentPosting.BuildContextAsync(db.Context, tracked,
                        FinancialDocumentPosting.CreateEvent, tracked.DocumentDate);
                    return await OfflinePostingLookup.PostAllAsync(engine, [(code, context)], user, token);
                }));
        }

        // Baixas (positivas, não estornadas)
        var settlements = await db.Context.FinancialSettlements.AsNoTracking()
            .Where(s => s.Amount > 0 && s.SettlementDate >= start && s.SettlementDate < end)
            .ToListAsync(ct);
        var settlementKeys = settlements.Select(s => s.Key).ToList();
        var reversed = (await db.Context.FinancialSettlements.AsNoTracking()
            .Where(s => s.ReversedSettlementKey != null && settlementKeys.Contains(s.ReversedSettlementKey.Value))
            .Select(s => s.ReversedSettlementKey!.Value).ToListAsync(ct)).ToHashSet();
        var documentKeys = settlements.Select(s => s.FinancialDocumentKey).Distinct().ToList();
        var documents = await db.Context.FinancialDocuments.AsNoTracking()
            .Where(d => documentKeys.Contains(d.Key)).ToDictionaryAsync(d => d.Key, ct);
        foreach (var settlement in settlements)
        {
            if (reversed.Contains(settlement.Key)) continue;
            if (!documents.TryGetValue(settlement.FinancialDocumentKey, out var document)) continue;
            if (!SettlementPosting.IsPostable(settlement, document)) continue;
            var branch = Branch(document.BranchCode, AccountBranch(settlement.FinancialAccountCode));
            if (!OfflinePostingLookup.InBranch(branch, branchCode)) continue;
            var code = document.Direction == FinancialDirection.Receivable ? "520" : "530";
            var key = settlement.Key.ToString();
            var missing = lookup.Missing(key, SettlementPosting.SettleEvent, code);
            if (missing.Count == 0) continue;
            var settlementKey = settlement.Key;
            events.Add(new OfflinePostingEvent(settlement.SettlementDate.Date, SettlementPosting.DocumentType, key,
                document.Code ?? key, branch, missing, null, async (engine, user, token) =>
                {
                    var tracked = await db.Context.FinancialSettlements.FirstAsync(s => s.Key == settlementKey, token);
                    var title = await db.Context.FinancialDocuments.FirstAsync(d => d.Key == tracked.FinancialDocumentKey, token);
                    var context = await SettlementPosting.BuildContextAsync(db.Context, tracked, title, tracked.Key,
                        SettlementPosting.SettleEvent);
                    return await OfflinePostingLookup.PostAllAsync(engine, [(code, context)], user, token);
                }));
        }

        // Lançamentos na conta (as pernas de transferência ficam no 560)
        var entries = await db.Context.FinancialAccountEntries.AsNoTracking()
            .Where(e => e.TransferKey == null && e.Amount != 0 && e.EntryDate >= start && e.EntryDate < end)
            .ToListAsync(ct);
        foreach (var entry in entries)
        {
            var branch = Branch(AccountBranch(entry.FinancialAccountCode));
            if (!OfflinePostingLookup.InBranch(branch, branchCode)) continue;
            var code = entry.Amount > 0 ? "563" : "562";
            var key = entry.Key.ToString();
            var missing = lookup.Missing(key, AccountEntryPosting.CreateEvent, code);
            if (missing.Count == 0) continue;
            var entryKey = entry.Key;
            events.Add(new OfflinePostingEvent(entry.EntryDate.Date, AccountEntryPosting.EntryDocumentType, key,
                entry.DocumentReference ?? entry.Description ?? key, branch, missing, null, async (engine, user, token) =>
                {
                    var tracked = await db.Context.FinancialAccountEntries.FirstAsync(e => e.Key == entryKey, token);
                    var context = await AccountEntryPosting.BuildEntryContextAsync(db.Context, tracked,
                        AccountEntryPosting.CreateEvent, tracked.EntryDate);
                    return await OfflinePostingLookup.PostAllAsync(engine, [(code, context)], user, token);
                }));
        }

        // Transferências
        var transfers = await db.Context.FinancialAccountTransfers.AsNoTracking()
            .Where(t => t.TransferDate >= start && t.TransferDate < end)
            .ToListAsync(ct);
        foreach (var transfer in transfers)
        {
            var branch = Branch(AccountBranch(transfer.FromAccountCode));
            if (!OfflinePostingLookup.InBranch(branch, branchCode)) continue;
            var key = transfer.Key.ToString();
            var missing = lookup.Missing(key, AccountEntryPosting.CreateEvent, "560");
            if (missing.Count == 0) continue;
            var transferKey = transfer.Key;
            events.Add(new OfflinePostingEvent(transfer.TransferDate.Date, AccountEntryPosting.TransferDocumentType, key,
                $"{transfer.FromAccountCode}>{transfer.ToAccountCode}", branch, missing, null, async (engine, user, token) =>
                {
                    var tracked = await db.Context.FinancialAccountTransfers.FirstAsync(t => t.Key == transferKey, token);
                    var context = await AccountEntryPosting.BuildTransferContextAsync(db.Context, tracked,
                        AccountEntryPosting.CreateEvent, tracked.TransferDate);
                    return await OfflinePostingLookup.PostAllAsync(engine, [("560", context)], user, token);
                }));
        }

        foreach (var e in events) yield return e;
    }
}
