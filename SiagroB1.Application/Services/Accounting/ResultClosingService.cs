using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Accounting;

/// <summary>Saldo (débito − crédito) de uma conta de resultado numa filial + CC + Item.</summary>
public sealed record ResultBalance(string BranchCode, string AccountCode, string? CostCenterCode, string? AccountingItemCode,
    decimal Balance);

public interface IResultClosingService
{
    /// <summary>Saldos ≠ 0 das contas de resultado (Receita/Despesa) até a data, por filial + conta + CC + Item.</summary>
    Task<List<ResultBalance>> UnclosedAsync(DateTime date, string? branchCode, CancellationToken ct);

    /// <summary>Um lançamento de apuração por filial com saldo (grava). Vazio = nada a apurar.</summary>
    Task<List<JournalEntry>> CloseAsync(DateTime date, string? branchCode, string? history, string user, CancellationToken ct);

    /// <summary>Estorna, por filial, a apuração viva mais recente, na data dela (grava).</summary>
    Task<List<JournalEntry>> ReverseLatestAsync(string? branchCode, string user, CancellationToken ct);
}

/// <summary>
/// Apuração de resultado (CTBA211). Os saldos são cumulativos desde a origem e cada apuração zera as contas, então o
/// saldo até a data É o resultado desde a última apuração. Zera por conta + CC + Item (Decisão 7 da spec) e põe a
/// contrapartida numa linha por conta de apuração, sem CC/Item.
/// </summary>
public class ResultClosingService(
    IUnitOfWork db,
    IJournalEntryPostingService posting,
    IJournalEntriesService entries,
    IAccountingSettingsService settings) : IResultClosingService
{
    public const string Batch = "008860";
    public const string DocumentType = "ResultClosing";
    public const string Event = "Close";

    public async Task<List<ResultBalance>> UnclosedAsync(DateTime date, string? branchCode, CancellationToken ct)
    {
        var resultAccounts = await db.Context.LedgerAccounts.AsNoTracking()
            .Where(a => a.Type == LedgerAccountType.Revenue || a.Type == LedgerAccountType.Expense)
            .Select(a => a.Code).ToListAsync(ct);
        if (resultAccounts.Count == 0) return [];

        var end = date.Date;
        var branch = string.IsNullOrWhiteSpace(branchCode) ? null : branchCode.Trim();
        var rows = await (
                from l in db.Context.JournalEntryLines.AsNoTracking()
                join e in db.Context.JournalEntries.AsNoTracking() on l.DocEntry equals e.DocEntry
                where e.PostingDate <= end && (branch == null || e.BranchCode == branch) &&
                      l.EntryType != JournalEntryLineType.HistoryContinuation &&
                      ((l.DebitAccountCode != null && resultAccounts.Contains(l.DebitAccountCode)) ||
                       (l.CreditAccountCode != null && resultAccounts.Contains(l.CreditAccountCode)))
                select new
                {
                    e.BranchCode, l.Amount, l.DebitAccountCode, l.CreditAccountCode, l.DebitCostCenterCode,
                    l.CreditCostCenterCode, l.DebitAccountingItemCode, l.CreditAccountingItemCode,
                })
            .ToListAsync(ct);

        var set = resultAccounts.ToHashSet();
        var sums = new Dictionary<(string, string, string?, string?), decimal>();
        foreach (var r in rows)
        {
            if (r.DebitAccountCode is { } d && set.Contains(d))
            {
                var k = (r.BranchCode, d, r.DebitCostCenterCode, r.DebitAccountingItemCode);
                sums[k] = sums.GetValueOrDefault(k) + r.Amount;
            }
            if (r.CreditAccountCode is { } c && set.Contains(c))
            {
                var k = (r.BranchCode, c, r.CreditCostCenterCode, r.CreditAccountingItemCode);
                sums[k] = sums.GetValueOrDefault(k) - r.Amount;
            }
        }

        return sums.Where(x => x.Value != 0)
            .Select(x => new ResultBalance(x.Key.Item1, x.Key.Item2, x.Key.Item3, x.Key.Item4, x.Value))
            .OrderBy(x => x.BranchCode, StringComparer.Ordinal).ThenBy(x => x.AccountCode, StringComparer.Ordinal)
            .ThenBy(x => x.CostCenterCode, StringComparer.Ordinal).ThenBy(x => x.AccountingItemCode, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<List<JournalEntry>> CloseAsync(DateTime date, string? branchCode, string? history, string user,
        CancellationToken ct)
    {
        var balances = await UnclosedAsync(date, branchCode, ct);
        if (balances.Count == 0) return [];

        var defaultClosing = (await settings.GetAsync()).DefaultResultClosingAccountCode;
        var codes = balances.Select(b => b.AccountCode).Distinct().ToList();
        var specific = await db.Context.LedgerAccounts.AsNoTracking().Where(a => codes.Contains(a.Code))
            .ToDictionaryAsync(a => a.Code, a => a.ResultClosingAccountCode, ct);
        string ClosingFor(string account)
        {
            var code = specific.GetValueOrDefault(account);
            if (string.IsNullOrWhiteSpace(code)) code = defaultClosing;
            if (string.IsNullOrWhiteSpace(code))
                throw new DefaultException($"Conta {account}: informe a conta de apuração (na conta ou nas Configurações Contábeis).");
            return code.Trim();
        }

        var text = string.IsNullOrWhiteSpace(history)
            ? string.Create(CultureInfo.InvariantCulture, $"Apuração de resultado {date:MM/yyyy}")
            : history.Trim();
        var result = new List<JournalEntry>();
        foreach (var branch in balances.GroupBy(b => b.BranchCode))
        {
            var lines = new List<JournalEntryLineDraft>();
            var counter = new Dictionary<string, decimal>();
            foreach (var b in branch)
            {
                var closing = ClosingFor(b.AccountCode);
                // Saldo devedor (despesa) zera com crédito; credor (receita) com débito.
                lines.Add(b.Balance > 0
                    ? new JournalEntryLineDraft
                    {
                        EntryType = JournalEntryLineType.Credit, CreditAccountCode = b.AccountCode, Amount = b.Balance,
                        History = text, CreditCostCenterCode = b.CostCenterCode, CreditAccountingItemCode = b.AccountingItemCode,
                    }
                    : new JournalEntryLineDraft
                    {
                        EntryType = JournalEntryLineType.Debit, DebitAccountCode = b.AccountCode, Amount = -b.Balance,
                        History = text, DebitCostCenterCode = b.CostCenterCode, DebitAccountingItemCode = b.AccountingItemCode,
                    });
                counter[closing] = counter.GetValueOrDefault(closing) + b.Balance;
            }

            foreach (var (closing, net) in counter.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                if (net == 0) continue;
                lines.Add(net > 0
                    ? new JournalEntryLineDraft { EntryType = JournalEntryLineType.Debit, DebitAccountCode = closing, Amount = net, History = text }
                    : new JournalEntryLineDraft { EntryType = JournalEntryLineType.Credit, CreditAccountCode = closing, Amount = -net, History = text });
            }

            result.Add(await posting.AddAsync(new JournalEntryDraft
            {
                PostingDate = date.Date,
                BranchCode = branch.Key,
                Batch = Batch,
                Origin = JournalEntryOrigin.ResultClosing,
                SourceDocumentType = DocumentType,
                SourceDocumentKey = date.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                SourceEvent = Event,
                Lines = lines,
            }, user, ct));
        }

        await db.SaveChangesAsync();
        return result;
    }

    public async Task<List<JournalEntry>> ReverseLatestAsync(string? branchCode, string user, CancellationToken ct)
    {
        var branch = string.IsNullOrWhiteSpace(branchCode) ? null : branchCode.Trim();
        var live = await db.Context.JournalEntries.Include(e => e.Lines)
            .Where(e => e.Origin == JournalEntryOrigin.ResultClosing && e.ReversedByDocEntry == null &&
                        (branch == null || e.BranchCode == branch))
            .ToListAsync(ct);
        var latest = live.GroupBy(e => e.BranchCode)
            .Select(g => g.OrderByDescending(e => e.PostingDate).ThenByDescending(e => e.DocEntry).First())
            .OrderBy(e => e.BranchCode, StringComparer.Ordinal).ToList();
        if (latest.Count == 0) throw new DefaultException("Não há apuração para estornar.");

        var result = new List<JournalEntry>();
        foreach (var original in latest)
            result.Add(await entries.AddReversalAsync(original, original.PostingDate, user));
        await db.SaveChangesAsync();
        return result;
    }
}
