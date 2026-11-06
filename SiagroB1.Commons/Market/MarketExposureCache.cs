using System.Collections.Concurrent;

namespace SiagroB1.Commons.Market;

/// <summary>
/// Cache curto (30 s), por empresa, da leitura da exposição física dos contratos. Singleton: a chave é
/// sempre o código da empresa, então uma empresa nunca recebe a leitura de outra. O valor é opaco aqui
/// (o tipo vive no Application, que o Commons não referencia).
/// </summary>
public sealed class MarketExposureCache(TimeProvider time)
{
    public static readonly TimeSpan Validity = TimeSpan.FromSeconds(30);

    private sealed record Entry(object Value, DateTimeOffset StoredAt);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public bool TryGet<T>(string companyCode, out T? value) where T : class
    {
        if (_entries.TryGetValue(companyCode, out var entry)
            && entry.Value is T typed
            && time.GetUtcNow() - entry.StoredAt < Validity)
        {
            value = typed;
            return true;
        }

        value = null;
        return false;
    }

    public void Set(string companyCode, object value) =>
        _entries[companyCode] = new Entry(value, time.GetUtcNow());

    public void Invalidate(string companyCode) => _entries.TryRemove(companyCode, out _);
}
