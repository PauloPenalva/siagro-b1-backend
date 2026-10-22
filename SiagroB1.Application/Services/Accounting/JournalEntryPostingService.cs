using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Accounting;

namespace SiagroB1.Application.Services.Accounting;

public interface IJournalEntryPostingService
{
    /// <summary>Valida, numera e adiciona o lançamento ao contexto. Não grava: quem chama faz o SaveChanges.</summary>
    Task<JournalEntry> AddAsync(JournalEntryDraft draft, string user, CancellationToken ct = default);
}

/// <summary>
/// Porta única de gravação de lançamento contábil (CTBGrava do Protheus). Garante débito = crédito, conta analítica
/// ativa e fora de bloqueio, amarração de CC/Item pela conta, valor positivo e período aberto.
/// </summary>
public class JournalEntryPostingService(IUnitOfWork db, AccountingPeriodGuard periodGuard) : IJournalEntryPostingService
{
    public const int HistoryLength = 40;
    private static readonly CultureInfo PtBr = new("pt-BR");

    public async Task<JournalEntry> AddAsync(JournalEntryDraft draft, string user, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(draft.BranchCode)) throw new DefaultException("Informe a filial do lançamento.");
        if (draft.Lines.Count == 0) throw new DefaultException("Informe as linhas do lançamento.");

        var date = draft.PostingDate.Date;
        await periodGuard.EnsureOpenAsync(date, ct);

        var codes = draft.Lines.SelectMany(l => new[] { l.DebitAccountCode, l.CreditAccountCode })
            .Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c!.Trim()).Distinct().ToList();
        var accounts = await db.Context.LedgerAccounts.AsNoTracking()
            .Where(a => codes.Contains(a.Code)).ToDictionaryAsync(a => a.Code, ct);

        for (var i = 0; i < draft.Lines.Count; i++)
        {
            try
            {
                await ValidateLineAsync(draft.Lines[i], i + 1, date, accounts);
            }
            catch (DefaultException e) when (draft.StandardPostingCode is not null && draft.Lines[i].StandardPostingSequence is not null)
            {
                throw new DefaultException($"LP {draft.StandardPostingCode} seq. {draft.Lines[i].StandardPostingSequence}: {e.Message}");
            }
        }

        var debit = draft.Lines.Where(l => l.EntryType is JournalEntryLineType.Debit or JournalEntryLineType.Double).Sum(l => l.Amount);
        var credit = draft.Lines.Where(l => l.EntryType is JournalEntryLineType.Credit or JournalEntryLineType.Double).Sum(l => l.Amount);
        if (debit != credit)
            throw new DefaultException(string.Format(PtBr, "Lançamento desbalanceado: débito {0:N2} x crédito {1:N2}.", debit, credit));

        var entry = new JournalEntry
        {
            PostingDate = date,
            BranchCode = draft.BranchCode.Trim(),
            Batch = draft.Batch.Trim(),
            SubBatch = string.IsNullOrWhiteSpace(draft.SubBatch) ? "001" : draft.SubBatch.Trim(),
            Origin = draft.Origin,
            StandardPostingCode = draft.StandardPostingCode,
            SourceDocumentType = draft.SourceDocumentType,
            SourceDocumentKey = draft.SourceDocumentKey,
            SourceEvent = draft.SourceEvent,
            ReversalOfDocEntry = draft.ReversalOfDocEntry,
            Comments = draft.Comments,
            CreatedAt = DateTime.Now,
            CreatedBy = user,
        };
        entry.DocumentNumber = await NextNumberAsync(entry.PostingDate, entry.Batch, entry.SubBatch, ct);

        var number = 0;
        foreach (var line in draft.Lines)
        {
            var chunks = SplitHistory(line.History);
            entry.Lines.Add(new JournalEntryLine
            {
                LineNumber = ++number,
                EntryType = line.EntryType,
                DebitAccountCode = Clean(line.DebitAccountCode),
                CreditAccountCode = Clean(line.CreditAccountCode),
                Amount = line.Amount,
                History = chunks[0],
                DebitCostCenterCode = Clean(line.DebitCostCenterCode),
                CreditCostCenterCode = Clean(line.CreditCostCenterCode),
                DebitAccountingItemCode = Clean(line.DebitAccountingItemCode),
                CreditAccountingItemCode = Clean(line.CreditAccountingItemCode),
                StandardPostingSequence = line.StandardPostingSequence,
                BusinessPartnerCode = Clean(line.BusinessPartnerCode),
            });
            foreach (var rest in chunks.Skip(1))
            {
                entry.Lines.Add(new JournalEntryLine
                {
                    LineNumber = ++number, EntryType = JournalEntryLineType.HistoryContinuation, History = rest,
                    StandardPostingSequence = line.StandardPostingSequence,
                });
            }
        }

        db.Context.JournalEntries.Add(entry);
        return entry;
    }

    private async Task ValidateLineAsync(JournalEntryLineDraft line, int n, DateTime date,
        IReadOnlyDictionary<string, LedgerAccount> accounts)
    {
        var hasDebit = !string.IsNullOrWhiteSpace(line.DebitAccountCode);
        var hasCredit = !string.IsNullOrWhiteSpace(line.CreditAccountCode);

        switch (line.EntryType)
        {
            case JournalEntryLineType.Debit when !hasDebit || hasCredit:
                throw new DefaultException($"Linha {n}: informe somente a conta de débito.");
            case JournalEntryLineType.Credit when !hasCredit || hasDebit:
                throw new DefaultException($"Linha {n}: informe somente a conta de crédito.");
            case JournalEntryLineType.Double when !hasDebit || !hasCredit:
                throw new DefaultException($"Linha {n}: informe as contas de débito e de crédito.");
            case JournalEntryLineType.HistoryContinuation:
                throw new DefaultException($"Linha {n}: a continuação de histórico é gerada automaticamente.");
        }

        if (line.Amount <= 0) throw new DefaultException($"Linha {n}: informe um valor maior que zero.");
        // A coluna é decimal(19,2): casas a mais seriam arredondadas no banco e desbalanceariam o que aqui fechou.
        if (line.Amount != Math.Round(line.Amount, 2))
            throw new DefaultException($"Linha {n}: o valor deve ter no máximo 2 casas decimais.");

        if (hasDebit)
            await ValidateSideAsync(n, date, accounts, line.DebitAccountCode!.Trim(), Clean(line.DebitCostCenterCode), Clean(line.DebitAccountingItemCode));
        if (hasCredit)
            await ValidateSideAsync(n, date, accounts, line.CreditAccountCode!.Trim(), Clean(line.CreditCostCenterCode), Clean(line.CreditAccountingItemCode));
    }

    private async Task ValidateSideAsync(int n, DateTime date, IReadOnlyDictionary<string, LedgerAccount> accounts,
        string code, string? costCenter, string? item)
    {
        if (!accounts.TryGetValue(code, out var account) || !LedgerAccountRules.IsPostable(account, date))
            throw new DefaultException(string.Format(PtBr,
                "Linha {0}: a conta {1} não existe, é sintética, está inativa ou bloqueada em {2:dd/MM/yyyy}.", n, code, date));

        if (costCenter is not null && !account.AcceptsCostCenter)
            throw new DefaultException($"Linha {n}: a conta {code} não aceita centro de custo.");
        if (costCenter is null && account.RequiresCostCenter)
            throw new DefaultException($"Linha {n}: a conta {code} exige centro de custo.");
        if (item is not null && !account.AcceptsAccountingItem)
            throw new DefaultException($"Linha {n}: a conta {code} não aceita item contábil.");
        if (item is null && account.RequiresAccountingItem)
            throw new DefaultException($"Linha {n}: a conta {code} exige item contábil.");

        if (costCenter is not null && !await AccountingAssignmentLookup.IsUsableCostCenterAsync(db, costCenter, date))
            throw new DefaultException(
                $"Linha {n}: o centro de custo {costCenter} não existe, é sintético, está bloqueado ou fora da vigência.");
        if (item is not null && !await AccountingAssignmentLookup.IsUsableAccountingItemAsync(db, item, date))
            throw new DefaultException(
                $"Linha {n}: o item contábil {item} não existe, é sintético, está bloqueado ou fora da vigência.");
    }

    /// <summary>Próximo número do documento (CTF/ProxDoc) para data + lote + sublote, contando os ainda não gravados.</summary>
    private async Task<string> NextNumberAsync(DateTime date, string batch, string subBatch, CancellationToken ct)
    {
        var stored = await db.Context.JournalEntries.AsNoTracking()
            .Where(e => e.PostingDate == date && e.Batch == batch && e.SubBatch == subBatch)
            .Select(e => e.DocumentNumber).ToListAsync(ct);
        var pending = db.Context.ChangeTracker.Entries<JournalEntry>()
            .Where(e => e.State == EntityState.Added && e.Entity.PostingDate == date && e.Entity.Batch == batch &&
                        e.Entity.SubBatch == subBatch)
            .Select(e => e.Entity.DocumentNumber);
        var max = stored.Concat(pending).Select(n => int.TryParse(n, out var v) ? v : 0).DefaultIfEmpty(0).Max();
        return (max + 1).ToString("000000", CultureInfo.InvariantCulture);
    }

    private static List<string> SplitHistory(string? history)
    {
        var text = (history ?? string.Empty).Trim();
        if (text.Length <= HistoryLength) return [text];
        var chunks = new List<string>();
        for (var i = 0; i < text.Length; i += HistoryLength)
            chunks.Add(text.Substring(i, Math.Min(HistoryLength, text.Length - i)));
        return chunks;
    }

    private static string? Clean(string? code) => string.IsNullOrWhiteSpace(code) ? null : code.Trim();
}
