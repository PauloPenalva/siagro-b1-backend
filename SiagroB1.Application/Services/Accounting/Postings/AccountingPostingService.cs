using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Accounting;

namespace SiagroB1.Application.Services.Accounting.Postings;

public interface IAccountingPostingService
{
    /// <summary>
    /// Contabiliza o evento pelo lançamento padrão do código. Só enfileira (quem chama grava). Devolve null quando
    /// não há o que lançar: SAPB1, sem data de corte, antes do corte, LP ausente/inativo, já lançado ou sem valor.
    /// </summary>
    Task<JournalEntry?> PostAsync(string code, PostingContext context, string user, CancellationToken ct = default);

    /// <summary>
    /// Estorno por código que estorna outro (655 estorna 650...). Sem lançamento vivo do original para o
    /// <paramref name="originalEvent"/> do documento, não faz nada. Com o LP de estorno inativo, inverte o original.
    /// </summary>
    Task<JournalEntry?> PostReversalAsync(string code, PostingContext context, string originalEvent, string user,
        CancellationToken ct = default);
}

/// <summary>
/// Motor da contabilização automática (DetProva + cA100Incl do Protheus): avalia as sequências do lançamento padrão
/// sobre o contexto do evento e entrega o lançamento ao <see cref="IJournalEntryPostingService"/>, que valida tudo.
/// </summary>
public class AccountingPostingService(
    IUnitOfWork db,
    IConfiguration configuration,
    IJournalEntryPostingService posting,
    IJournalEntriesService entries,
    AccountingPeriodGuard periodGuard,
    IAccountingSettingsService settings,
    DistributionRuleExpander expander) : IAccountingPostingService
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    public async Task<JournalEntry?> PostAsync(string code, PostingContext context, string user, CancellationToken ct = default)
    {
        var definition = StandardPostingCatalog.Find(code)
                         ?? throw new InvalidOperationException($"Código de lançamento padrão {code} fora do catálogo.");
        if (!await IsEnabledForAsync(context.Date)) return null;
        if (await FindLiveAsync(code, context.DocumentType, context.DocumentKey, context.Event, ct) is not null) return null;

        var lp = await ActiveLpAsync(code, ct);
        if (lp is null) return null;

        var draft = await BuildDraftAsync(definition, lp, context, ct);
        return draft is null ? null : await posting.AddAsync(draft, user, ct);
    }

    public async Task<JournalEntry?> PostReversalAsync(string code, PostingContext context, string originalEvent, string user,
        CancellationToken ct = default)
    {
        var definition = StandardPostingCatalog.Find(code)
                         ?? throw new InvalidOperationException($"Código de lançamento padrão {code} fora do catálogo.");
        if (definition.Reverses is null)
            throw new InvalidOperationException($"O código {code} não estorna outro código.");
        if (!ErpMode.IsStandalone(configuration)) return null;

        // O original pode ser de antes do corte de hoje: o que decide é existir lançamento vivo dele.
        var original = await FindLiveAsync(definition.Reverses, context.DocumentType, context.DocumentKey, originalEvent, ct);
        if (original is null) return null;

        var lp = await ActiveLpAsync(code, ct);
        if (lp is null) return await entries.AddReversalAsync(original, await PostingDateAsync(context, ct), user);

        var draft = await BuildDraftAsync(definition, lp, context, ct);
        if (draft is null) return await entries.AddReversalAsync(original, await PostingDateAsync(context, ct), user);

        draft.ReversalOfDocEntry = original.DocEntry == 0 ? null : original.DocEntry;
        var reversal = await posting.AddAsync(draft, user, ct);
        reversal.ReversalOf = original;
        original.ReversedBy = reversal;
        return reversal;
    }

    private async Task<bool> IsEnabledForAsync(DateTime date)
    {
        if (!ErpMode.IsStandalone(configuration)) return false;
        var cutover = (await settings.GetAsync()).CutoverDate;
        return cutover is not null && date.Date >= cutover.Value.Date;
    }

    private Task<StandardPosting?> ActiveLpAsync(string code, CancellationToken ct) =>
        db.Context.StandardPostings.AsNoTracking().Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Code == code && p.Active, ct);

    /// <summary>Lançamento vivo (não estorno, não estornado) do código para o documento/evento — no banco ou ainda pendente.</summary>
    private async Task<JournalEntry?> FindLiveAsync(string code, string type, string key, string evt, CancellationToken ct)
    {
        var pending = db.Context.ChangeTracker.Entries<JournalEntry>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Unchanged)
            .Select(e => e.Entity)
            .FirstOrDefault(e => e.StandardPostingCode == code && e.SourceDocumentType == type && e.SourceDocumentKey == key &&
                                 e.SourceEvent == evt && e.ReversalOfDocEntry == null && e.ReversalOf == null &&
                                 e.ReversedByDocEntry == null && e.ReversedBy == null);
        if (pending is not null) return pending;

        return await db.Context.JournalEntries.Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.StandardPostingCode == code && e.SourceDocumentType == type &&
                                      e.SourceDocumentKey == key && e.SourceEvent == evt &&
                                      e.ReversalOfDocEntry == null && e.ReversedByDocEntry == null, ct);
    }

    private async Task<DateTime> PostingDateAsync(PostingContext context, CancellationToken ct) =>
        context.ExternalEvent ? await periodGuard.FirstOpenDateOnOrAfterAsync(context.Date.Date, ct) : context.Date.Date;

    private async Task<JournalEntryDraft?> BuildDraftAsync(StandardPostingDefinition definition, StandardPosting lp,
        PostingContext context, CancellationToken ct)
    {
        var date = await PostingDateAsync(context, ct);
        var suffix = date != context.Date.Date
            ? string.Format(PtBr, " (evento de {0:dd/MM/yyyy})", context.Date)
            : string.Empty;

        var rows = definition.PerLine ? context.Rows : context.Rows.Take(1).ToList();
        var lines = new List<JournalEntryLineDraft>();
        foreach (var row in rows)
        {
            foreach (var seq in lp.Lines.Where(l => l.Active).OrderBy(l => l.Sequence, StringComparer.Ordinal))
            {
                var value = Math.Round(row.Values.GetValueOrDefault(seq.ValueSource), 2, MidpointRounding.AwayFromZero);
                if (value <= 0) continue;

                var debit = seq.DebitAccountSource is null ? null : ResolveAccount(definition, seq, row, true);
                var credit = seq.CreditAccountSource is null ? null : ResolveAccount(definition, seq, row, false);
                var history = Fill(seq.HistoryTemplate, row, context) + suffix;

                foreach (var (amount, cc, item) in await SplitAsync(row, value, date, ct))
                {
                    lines.Add(new JournalEntryLineDraft
                    {
                        EntryType = seq.EntryType,
                        DebitAccountCode = debit,
                        CreditAccountCode = credit,
                        Amount = amount,
                        History = history,
                        DebitCostCenterCode = debit is null ? null : Dimension(seq.DebitCostCenterSource, seq.DebitCostCenterCode, cc),
                        CreditCostCenterCode = credit is null ? null : Dimension(seq.CreditCostCenterSource, seq.CreditCostCenterCode, cc),
                        DebitAccountingItemCode = debit is null ? null : Dimension(seq.DebitItemSource, seq.DebitItemCode, item),
                        CreditAccountingItemCode = credit is null ? null : Dimension(seq.CreditItemSource, seq.CreditItemCode, item),
                        StandardPostingSequence = seq.Sequence,
                        BusinessPartnerCode = context.PartnerCode,
                    });
                }
            }
        }

        if (lines.Count == 0) return null;

        return new JournalEntryDraft
        {
            PostingDate = date,
            BranchCode = context.BranchCode,
            Batch = definition.Batch,
            Origin = JournalEntryOrigin.StandardPosting,
            StandardPostingCode = definition.Code,
            SourceDocumentType = context.DocumentType,
            SourceDocumentKey = context.DocumentKey,
            SourceEvent = context.Event,
            Lines = lines,
        };
    }

    /// <summary>Conta pela fonte escolhida; vazia, tenta as fontes seguintes do catálogo até a conta fixa da sequência.</summary>
    private static string ResolveAccount(StandardPostingDefinition definition, StandardPostingLine seq, PostingRow row, bool debit)
    {
        var source = debit ? seq.DebitAccountSource! : seq.CreditAccountSource!;
        var fixedCode = debit ? seq.DebitAccountCode : seq.CreditAccountCode;
        var start = Math.Max(0, IndexOf(definition.AccountSources, source));
        for (var i = start; i < definition.AccountSources.Count; i++)
        {
            var candidate = definition.AccountSources[i] == StandardPostingCatalog.Fixed
                ? fixedCode
                : row.Accounts.GetValueOrDefault(definition.AccountSources[i]);
            if (!string.IsNullOrWhiteSpace(candidate)) return candidate.Trim();
        }

        var what = row.Placeholders.GetValueOrDefault("{ItemCode}") ?? row.Placeholders.GetValueOrDefault("{DocNumber}") ?? "o documento";
        throw new DefaultException($"LP {definition.Code} seq. {seq.Sequence}: conta de {(debit ? "débito" : "crédito")} não encontrada para {what}.");
    }

    private static int IndexOf(IReadOnlyList<string> list, string value)
    {
        for (var i = 0; i < list.Count; i++) if (list[i] == value) return i;
        return -1;
    }

    private static string? Dimension(DimensionSource source, string? fixedCode, string? fromDocument) => source switch
    {
        DimensionSource.Document => fromDocument,
        DimensionSource.Fixed => fixedCode,
        _ => null,
    };

    /// <summary>Rateio do documento expandido em partes por CC+Item; a sobra do arredondamento fica na última.</summary>
    private async Task<List<(decimal Amount, string? CostCenter, string? Item)>> SplitAsync(PostingRow row, decimal value,
        DateTime date, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(row.DistributionRuleCode))
            return [(value, row.CostCenterCode, row.AccountingItemCode)];

        var parts = await expander.ExpandAsync(row.DistributionRuleCode, date);
        if (parts.Count == 0) return [(value, row.CostCenterCode, row.AccountingItemCode)];

        var result = new List<(decimal, string?, string?)>();
        var used = 0m;
        for (var i = 0; i < parts.Count; i++)
        {
            var amount = i == parts.Count - 1
                ? value - used
                : Math.Round(value * parts[i].Percentage / 100m, 2, MidpointRounding.AwayFromZero);
            used += amount;
            if (amount > 0) result.Add((amount, parts[i].CostCenterCode, parts[i].AccountingItemCode));
        }

        return result;
    }

    private static string Fill(string template, PostingRow row, PostingContext context)
    {
        var text = template ?? string.Empty;
        foreach (var (key, value) in row.Placeholders) text = text.Replace(key, value);
        return text.Replace("{Date}", context.Date.ToString("dd/MM/yyyy", PtBr)).Trim();
    }
}
