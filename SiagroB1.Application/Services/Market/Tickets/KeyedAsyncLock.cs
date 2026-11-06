namespace SiagroB1.Application.Services.Market.Tickets;

/// <summary>
/// Exclusão mútua assíncrona por chave (singleton). Cada chave tem um SemaphoreSlim com contagem de referências,
/// removido quando o último interessado libera, para o dicionário não crescer.
/// Garante exclusão só DENTRO DO PROCESSO: vale porque o produto roda um processo Web por cliente. Com mais de uma
/// instância, o RowVersion da boleta continua sendo a segunda linha de defesa.
/// </summary>
public sealed class KeyedAsyncLock
{
    private sealed class Entry
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int References;
    }

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        Entry entry;
        lock (_entries)
        {
            if (!_entries.TryGetValue(key, out entry!))
                _entries[key] = entry = new Entry();
            entry.References++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
        }
        catch
        {
            Release(key, entry, held: false);
            throw;
        }

        return new Releaser(this, key, entry);
    }

    /// <summary>Chaves com interessados no momento (para teste).</summary>
    public int ActiveKeys
    {
        get { lock (_entries) return _entries.Count; }
    }

    private void Release(string key, Entry entry, bool held)
    {
        if (held)
            entry.Semaphore.Release();
        lock (_entries)
        {
            if (--entry.References == 0)
                _entries.Remove(key);
        }
    }

    private sealed class Releaser(KeyedAsyncLock owner, string key, Entry entry) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.Release(key, entry, held: true);
        }
    }
}
