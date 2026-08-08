using System.Collections.Concurrent;
using System.Net.WebSockets;
using SiagroB1.Commons.Scales;

namespace SiagroB1.Web.Sockets.TruckScale;

/// <summary>
/// Conexões vivas do SiagroB1.Client e leitura corrente de cada balança.
///
/// ConcurrentDictionary, e não Dictionary: com duas balanças, duas conexões escrevem aqui ao
/// mesmo tempo enquanto o SSE lê - o dicionário comum corrompia silenciosamente.
/// </summary>
public class TruckScaleHub(LiveReadingStore readings)
{
    private readonly ConcurrentDictionary<string, WebSocket> _connections = new(StringComparer.OrdinalIgnoreCase);

    public void Add(string scaleCode, WebSocket socket) => _connections[scaleCode] = socket;

    public void Remove(string scaleCode)
    {
        _connections.TryRemove(scaleCode, out _);
        readings.SetOffline(scaleCode);
    }

    public bool IsConnected(string scaleCode) => _connections.ContainsKey(scaleCode);

    public void PushWeight(string scaleCode, int weight) =>
        readings.Push(scaleCode, weight, DateTime.Now);

    public void SetOffline(string scaleCode) => readings.SetOffline(scaleCode);

    public LiveWeight GetLive(string scaleCode) => readings.Get(scaleCode, DateTime.Now);
}
