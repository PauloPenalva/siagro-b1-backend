using System.Collections.Concurrent;

namespace SiagroB1.Commons.Market;

public sealed record LiveQuoteSnapshot(long Version, CmaConnectionStatus Status, string? Message, DateTime? LastChangedAt);

/// <summary>
/// Sinalizador em memória, por empresa, de que as cotações mudaram. O banco é a única fonte das
/// cotações: o store guarda só a versão (para o SSE saber quando reler o quadro), o estado da
/// conexão com a CMA e o pedido de reconexão.
/// </summary>
public sealed class LiveQuoteStore
{
    private sealed class CompanyState
    {
        public readonly object Gate = new();
        public long Version;
        public int ReconnectRequested;
        public CmaConnectionStatus Status = CmaConnectionStatus.Disabled;
        public string? Message;
        public DateTime? LastChangedAt;
    }

    private readonly ConcurrentDictionary<string, CompanyState> _companies = new(StringComparer.OrdinalIgnoreCase);

    private CompanyState State(string companyCode) => _companies.GetOrAdd(companyCode, _ => new CompanyState());

    public void NotifyChanged(string companyCode)
    {
        var state = State(companyCode);
        lock (state.Gate)
        {
            state.Version++;
            state.LastChangedAt = DateTime.UtcNow;
        }
    }

    public void SetStatus(string companyCode, CmaConnectionStatus status, string? message)
    {
        var state = State(companyCode);
        lock (state.Gate)
        {
            if (state.Status == status && state.Message == message)
                return;
            state.Status = status;
            state.Message = message;
            state.Version++;
        }
    }

    public LiveQuoteSnapshot Get(string companyCode)
    {
        if (!_companies.TryGetValue(companyCode, out var state))
            return new LiveQuoteSnapshot(0, CmaConnectionStatus.Disabled, null, null);

        lock (state.Gate)
            return new LiveQuoteSnapshot(state.Version, state.Status, state.Message, state.LastChangedAt);
    }

    public void RequestReconnect(string companyCode) => Interlocked.Exchange(ref State(companyCode).ReconnectRequested, 1);

    /// <summary>Verdadeiro uma única vez por pedido de reconexão.</summary>
    public bool TryConsumeReconnect(string companyCode) =>
        _companies.TryGetValue(companyCode, out var state) && Interlocked.Exchange(ref state.ReconnectRequested, 0) == 1;
}
