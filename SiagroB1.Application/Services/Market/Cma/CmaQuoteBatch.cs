using SiagroB1.Commons.Market;

namespace SiagroB1.Application.Services.Market.Cma;

/// <summary>
/// Lote em memória das cotações vindas do canal, uma por símbolo: a mais nova vence campo a campo
/// (o stream manda deltas, então campo nulo mantém o anterior). Thread-safe: o canal acumula e o
/// flush retira de outra tarefa.
/// </summary>
public sealed class CmaQuoteBatch
{
    private readonly object _gate = new();
    private Dictionary<string, MarketQuoteUpdate> _pending = new(StringComparer.OrdinalIgnoreCase);

    public void Add(MarketQuoteUpdate update)
    {
        lock (_gate)
            _pending[update.Symbol] = _pending.TryGetValue(update.Symbol, out var older) ? Merge(older, update) : update;
    }

    /// <summary>Retira tudo o que está pendente (vazio se nada chegou).</summary>
    public IReadOnlyCollection<MarketQuoteUpdate> TakeAll()
    {
        lock (_gate)
        {
            if (_pending.Count == 0)
                return [];
            var taken = _pending.Values.ToList();
            _pending = new Dictionary<string, MarketQuoteUpdate>(StringComparer.OrdinalIgnoreCase);
            return taken;
        }
    }

    /// <summary>Devolve um lote que não foi gravado; o que chegou depois continua valendo por cima.</summary>
    public void Restore(IEnumerable<MarketQuoteUpdate> failed)
    {
        lock (_gate)
            foreach (var older in failed)
                _pending[older.Symbol] = _pending.TryGetValue(older.Symbol, out var newer) ? Merge(older, newer) : older;
    }

    public static MarketQuoteUpdate Merge(MarketQuoteUpdate older, MarketQuoteUpdate newer) => new(
        newer.Symbol,
        newer.Last ?? older.Last,
        newer.Bid ?? older.Bid,
        newer.Ask ?? older.Ask,
        newer.ChangePercent ?? older.ChangePercent,
        newer.High ?? older.High,
        newer.Low ?? older.Low,
        newer.Open ?? older.Open,
        newer.PreviousClose ?? older.PreviousClose,
        newer.Last.HasValue ? newer.IsSettlement : older.IsSettlement,
        newer.QuotedAt ?? older.QuotedAt);
}
