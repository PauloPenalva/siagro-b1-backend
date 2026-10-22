using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Reports.Services.Accounting;

/// <summary>Uma partida numa conta: o lado débito ou crédito de uma linha de lançamento.</summary>
public sealed record AccountPosting(
    string AccountCode,
    DateTime Date,
    int DocEntry,
    int LineNumber,
    string Batch,
    string DocumentNumber,
    string BranchCode,
    decimal Debit,
    decimal Credit,
    string? Counterpart,
    string History,
    string? CostCenterCode,
    string? AccountingItemCode);

/// <summary>Filtros comuns de Razão e Balancete.</summary>
public sealed record AccountPostingFilter(
    string? BranchCode = null,
    string? AccountFrom = null,
    string? AccountTo = null,
    string? CostCenterCode = null,
    string? AccountingItemCode = null);

/// <summary>
/// Lê as linhas de lançamento como partidas por conta (sem tabela de saldo). Linha tipo 3 vira duas partidas;
/// as linhas tipo 4 (continuação) entram só no histórico da linha anterior.
/// </summary>
public class AccountPostingReader(IUnitOfWork db)
{
    public async Task<List<AccountPosting>> ReadAsync(DateTime? from, DateTime to, AccountPostingFilter filter)
    {
        var query =
            from l in db.Context.JournalEntryLines.AsNoTracking()
            join e in db.Context.JournalEntries.AsNoTracking() on l.DocEntry equals e.DocEntry
            where e.PostingDate <= to.Date
            select new { Line = l, e.PostingDate, e.Batch, e.DocumentNumber, e.BranchCode };
        if (from is not null) query = query.Where(x => x.PostingDate >= from.Value.Date);
        if (!string.IsNullOrWhiteSpace(filter.BranchCode)) query = query.Where(x => x.BranchCode == filter.BranchCode);

        var raw = await query.ToListAsync();

        var postings = new List<AccountPosting>();
        foreach (var doc in raw.GroupBy(x => x.Line.DocEntry))
        {
            var lines = doc.OrderBy(x => x.Line.LineNumber).ToList();
            for (var i = 0; i < lines.Count; i++)
            {
                var x = lines[i];
                var l = x.Line;
                if (l.EntryType == JournalEntryLineType.HistoryContinuation) continue;

                var history = l.History;
                for (var j = i + 1; j < lines.Count && lines[j].Line.EntryType == JournalEntryLineType.HistoryContinuation; j++)
                    history += lines[j].Line.History;

                var isDouble = l.EntryType == JournalEntryLineType.Double;
                if (l.DebitAccountCode is not null)
                    postings.Add(new AccountPosting(l.DebitAccountCode, x.PostingDate, l.DocEntry, l.LineNumber, x.Batch,
                        x.DocumentNumber, x.BranchCode, l.Amount, 0m, isDouble ? l.CreditAccountCode : null, history,
                        l.DebitCostCenterCode, l.DebitAccountingItemCode));
                if (l.CreditAccountCode is not null)
                    postings.Add(new AccountPosting(l.CreditAccountCode, x.PostingDate, l.DocEntry, l.LineNumber, x.Batch,
                        x.DocumentNumber, x.BranchCode, 0m, l.Amount, isDouble ? l.DebitAccountCode : null, history,
                        l.CreditCostCenterCode, l.CreditAccountingItemCode));
            }
        }

        return postings.Where(p => Matches(p, filter)).ToList();
    }

    private static bool Matches(AccountPosting p, AccountPostingFilter f) =>
        (string.IsNullOrWhiteSpace(f.AccountFrom) || string.CompareOrdinal(p.AccountCode, f.AccountFrom.Trim()) >= 0) &&
        (string.IsNullOrWhiteSpace(f.AccountTo) || string.CompareOrdinal(p.AccountCode, f.AccountTo.Trim()) <= 0) &&
        (string.IsNullOrWhiteSpace(f.CostCenterCode) || p.CostCenterCode == f.CostCenterCode.Trim()) &&
        (string.IsNullOrWhiteSpace(f.AccountingItemCode) || p.AccountingItemCode == f.AccountingItemCode.Trim());

    /// <summary>Saldo exibido: valor absoluto + "D"/"C" pelo sinal (débito − crédito).</summary>
    public static (decimal Value, string Side) Display(decimal signed) =>
        (Math.Abs(signed), signed > 0 ? "D" : signed < 0 ? "C" : string.Empty);
}
