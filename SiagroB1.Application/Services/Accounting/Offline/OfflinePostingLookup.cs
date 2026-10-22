using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Accounting.Postings;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Accounting.Offline;

/// <summary>O que já está contabilizado e quais LPs estão ativos, carregados uma vez por coletor.</summary>
public sealed class OfflinePostingLookup
{
    public const string NoNetEffect = "Cancelado depois — sem efeito líquido";
    public const string DocumentDateUsed = "Data do documento usada";

    private readonly HashSet<string> _active;
    private readonly HashSet<(string Code, string Key, string Event)> _live;
    private readonly HashSet<string> _anyKey;

    private OfflinePostingLookup(HashSet<string> active, HashSet<(string, string, string)> live, HashSet<string> anyKey)
    {
        _active = active;
        _live = live;
        _anyKey = anyKey;
    }

    public static async Task<OfflinePostingLookup> LoadAsync(IUnitOfWork db, IReadOnlyCollection<string> documentTypes,
        CancellationToken ct)
    {
        var active = await db.Context.StandardPostings.AsNoTracking().Where(p => p.Active).Select(p => p.Code).ToListAsync(ct);
        var rows = await db.Context.JournalEntries.AsNoTracking()
            .Where(e => e.SourceDocumentType != null && documentTypes.Contains(e.SourceDocumentType))
            .Select(e => new { e.StandardPostingCode, e.SourceDocumentKey, e.SourceEvent, Live = e.ReversalOfDocEntry == null && e.ReversedByDocEntry == null })
            .ToListAsync(ct);
        return new OfflinePostingLookup(
            active.ToHashSet(),
            rows.Where(r => r.Live && r.StandardPostingCode != null)
                .Select(r => (r.StandardPostingCode!, r.SourceDocumentKey ?? string.Empty, r.SourceEvent ?? string.Empty)).ToHashSet(),
            rows.Select(r => r.SourceDocumentKey ?? string.Empty).ToHashSet());
    }

    /// <summary>Códigos candidatos com LP ativo e sem lançamento vivo para o documento/evento.</summary>
    public IReadOnlyList<string> Missing(string key, string evt, params string[] codes) =>
        codes.Where(c => _active.Contains(c) && !_live.Contains((c, key, evt))).ToList();

    /// <summary>Algum lançamento (vivo ou estornado) já referencia o documento.</summary>
    public bool HasAny(string key) => _anyKey.Contains(key);

    public static bool InBranch(string resolved, string? filter) =>
        string.IsNullOrWhiteSpace(filter) || string.Equals(resolved, filter.Trim(), StringComparison.OrdinalIgnoreCase);

    public static async Task<IReadOnlyList<JournalEntry>> PostAllAsync(IAccountingPostingService engine,
        IEnumerable<(string Code, PostingContext Context)> postings, string user, CancellationToken ct)
    {
        var posted = new List<JournalEntry>();
        foreach (var (code, context) in postings)
            if (await engine.PostAsync(code, context, user, ct) is { } entry) posted.Add(entry);
        return posted;
    }
}
