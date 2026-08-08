using System.Collections.Concurrent;

namespace SiagroB1.Commons.Scales;

/// <summary>
/// Leitura corrente de cada balança. Vive em memória, é escrita pela conexão WebSocket do Client e
/// lida pelo SSE e pela captura - por isso cada entrada tem seu próprio lock.
/// </summary>
public sealed class LiveReadingStore(TimeSpan window, int minimumSamples, TimeSpan offlineAfter)
{
    private sealed class Entry
    {
        public required StabilityDetector Detector { get; init; }

        public DateTime LastReadingAt { get; set; }
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public void Push(string scaleCode, int weight, DateTime now)
    {
        var entry = _entries.GetOrAdd(
            scaleCode,
            _ => new Entry { Detector = new StabilityDetector(window, minimumSamples) });

        lock (entry)
        {
            entry.Detector.Add(weight, now);
            entry.LastReadingAt = now;
        }
    }

    /// <summary>Marca a balança como offline na hora - usado quando o TCP do indicador cai.</summary>
    public void SetOffline(string scaleCode) => _entries.TryRemove(scaleCode, out _);

    public LiveWeight Get(string scaleCode, DateTime now)
    {
        if (!_entries.TryGetValue(scaleCode, out var entry))
            return new LiveWeight(0, false, false);

        lock (entry)
        {
            var online = now - entry.LastReadingAt <= offlineAfter;

            return online
                ? new LiveWeight(entry.Detector.Current, entry.Detector.IsStable(now), true)
                : new LiveWeight(0, false, false);
        }
    }
}
