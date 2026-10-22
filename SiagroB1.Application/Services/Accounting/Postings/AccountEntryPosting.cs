using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Accounting.Postings;

/// <summary>
/// Lançamento manual na conta financeira (563 entrada / 562 saída; exclusão 565/564) e transferência entre contas
/// (560; exclusão 561). As pernas da transferência não passam pelo 562/563: o 560 já cobre as duas contas.
/// </summary>
public static class AccountEntryPosting
{
    public const string EntryDocumentType = "FinancialAccountEntry";
    public const string TransferDocumentType = "FinancialAccountTransfer";
    public const string CreateEvent = "Create";
    public const string CancelEvent = "Cancel";

    public static async Task CreateEntryAsync(IAccountingPostingService? engine, AppDbContext db, FinancialAccountEntry entry,
        string user)
    {
        if (engine is null || entry.TransferKey is not null || entry.Amount == 0) return;
        await engine.PostAsync(entry.Amount > 0 ? "563" : "562", await EntryContextAsync(db, entry, CreateEvent, entry.EntryDate), user);
    }

    public static async Task CancelEntryAsync(IAccountingPostingService? engine, AppDbContext db, FinancialAccountEntry entry,
        string user)
    {
        if (engine is null || entry.TransferKey is not null || entry.Amount == 0) return;
        await engine.PostReversalAsync(entry.Amount > 0 ? "565" : "564",
            await EntryContextAsync(db, entry, CancelEvent, DateTime.Today), CreateEvent, user);
    }

    public static async Task CreateTransferAsync(IAccountingPostingService? engine, AppDbContext db,
        FinancialAccountTransfer transfer, string user)
    {
        if (engine is null) return;
        await engine.PostAsync("560", await TransferContextAsync(db, transfer, CreateEvent, transfer.TransferDate), user);
    }

    public static async Task CancelTransferAsync(IAccountingPostingService? engine, AppDbContext db,
        FinancialAccountTransfer transfer, string user)
    {
        if (engine is null) return;
        await engine.PostReversalAsync("561", await TransferContextAsync(db, transfer, CancelEvent, DateTime.Today), CreateEvent, user);
    }

    private static Task<(string? Ledger, string? Branch)> AccountAsync(AppDbContext db, string? code) =>
        db.FinancialAccounts.AsNoTracking().Where(a => a.Code == code)
            .Select(a => new ValueTuple<string?, string?>(a.LedgerAccountCode, a.BranchCode))
            .FirstOrDefaultAsync();

    private static async Task<PostingContext> EntryContextAsync(AppDbContext db, FinancialAccountEntry entry, string evt,
        DateTime date)
    {
        var (ledger, branch) = await AccountAsync(db, entry.FinancialAccountCode);
        var row = new PostingRow(
            new Dictionary<string, string?>
            {
                ["FinancialAccount.LedgerAccount"] = ledger,
                ["Document.LedgerAccount"] = entry.LedgerAccountCode,
            },
            new Dictionary<string, decimal> { ["Entry.Amount"] = Math.Abs(entry.Amount) },
            entry.CostCenterCode, entry.AccountingItemCode, entry.DistributionRuleCode,
            new Dictionary<string, string>
            {
                ["{DocNumber}"] = entry.DocumentReference ?? string.Empty, ["{Description}"] = entry.Description ?? string.Empty,
            });
        return new PostingContext(EntryDocumentType, entry.Key.ToString(), evt, date.Date,
            await PostingBranch.ResolveAsync(db, branch), null, [row]);
    }

    private static async Task<PostingContext> TransferContextAsync(AppDbContext db, FinancialAccountTransfer transfer,
        string evt, DateTime date)
    {
        var (fromLedger, fromBranch) = await AccountAsync(db, transfer.FromAccountCode);
        var (toLedger, _) = await AccountAsync(db, transfer.ToAccountCode);
        var row = new PostingRow(
            new Dictionary<string, string?>
            {
                ["FinancialAccount.LedgerAccount"] = fromLedger,
                ["TargetFinancialAccount.LedgerAccount"] = toLedger,
            },
            new Dictionary<string, decimal> { ["Transfer.Amount"] = Math.Abs(transfer.Amount) },
            null, null, null,
            new Dictionary<string, string>
            {
                ["{DocNumber}"] = $"{transfer.FromAccountCode}>{transfer.ToAccountCode}",
                ["{Description}"] = transfer.Description ?? string.Empty,
            });
        return new PostingContext(TransferDocumentType, transfer.Key.ToString(), evt, date.Date,
            await PostingBranch.ResolveAsync(db, fromBranch), null, [row]);
    }
}
